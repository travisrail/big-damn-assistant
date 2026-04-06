using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace BigDamnAssistant.Infrastructure;

public class EmailMonitoringService : IEmailMonitoringService
{
    private const string ClaudeModel = "claude-sonnet-4-20250514";
    private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";

    private readonly IEmailMonitoringRepository _emailMonitoringRepository;
    private readonly IFamilyMemberRepository _familyMemberRepository;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ICalendarService _calendarService;
    private readonly IReminderService _reminderService;
    private readonly GraphServiceClient _graphClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailMonitoringService> _logger;

    public EmailMonitoringService(
        IEmailMonitoringRepository emailMonitoringRepository,
        IFamilyMemberRepository familyMemberRepository,
        IWhatsAppService whatsAppService,
        ICalendarService calendarService,
        IReminderService reminderService,
        GraphServiceClient graphClient,
        IHttpClientFactory httpClientFactory,
        ILogger<EmailMonitoringService> logger)
    {
        _emailMonitoringRepository = emailMonitoringRepository;
        _familyMemberRepository = familyMemberRepository;
        _whatsAppService = whatsAppService;
        _calendarService = calendarService;
        _reminderService = reminderService;
        _graphClient = graphClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ScanMailboxesAsync(CancellationToken cancellationToken = default)
    {
        var mailboxes = await _emailMonitoringRepository.GetActiveMailboxesAsync(cancellationToken);
        if (mailboxes.Count == 0)
        {
            _logger.LogInformation("No active mailboxes to scan");
            return;
        }

        var senders = await _emailMonitoringRepository.GetActiveSendersAsync(cancellationToken);
        if (senders.Count == 0)
        {
            _logger.LogInformation("No whitelisted senders configured");
            return;
        }

        foreach (var mailbox in mailboxes)
        {
            try
            {
                await ScanSingleMailboxAsync(mailbox, senders, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan mailbox {Email}", mailbox.EmailAddress);
            }
        }
    }

    private async Task ScanSingleMailboxAsync(
        MonitoredMailbox mailbox,
        IReadOnlyList<WhitelistedSender> senders,
        CancellationToken cancellationToken)
    {
        var scanState = await _emailMonitoringRepository.GetScanStateAsync(mailbox.EmailAddress, cancellationToken)
            ?? new MailboxScanState
            {
                Id = $"scanstate-{mailbox.EmailAddress}",
                EmailAddress = mailbox.EmailAddress,
                LastScannedAt = DateTime.UtcNow.AddHours(-24)
            };

        _logger.LogInformation("Scanning mailbox {Email} since {Since}",
            mailbox.EmailAddress, scanState.LastScannedAt);

        var messages = await _graphClient.Users[mailbox.EmailAddress].Messages
            .GetAsync(config =>
            {
                config.QueryParameters.Filter = $"receivedDateTime ge {scanState.LastScannedAt:O}";
                config.QueryParameters.Select = new[] { "id", "subject", "from", "receivedDateTime", "bodyPreview" };
                config.QueryParameters.Top = 50;
                config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
            }, cancellationToken);

        if (messages?.Value == null || messages.Value.Count == 0)
        {
            _logger.LogInformation("No new emails found in {Email}", mailbox.EmailAddress);
            scanState.LastScannedAt = DateTime.UtcNow;
            await _emailMonitoringRepository.UpsertScanStateAsync(scanState, cancellationToken);
            return;
        }

        // Filter to only whitelisted senders (supports both exact email and domain matching)
        var matchingMessages = messages.Value.Where(m =>
        {
            var fromEmail = m.From?.EmailAddress?.Address;
            if (string.IsNullOrEmpty(fromEmail)) return false;
            return IsWhitelistedSender(fromEmail, senders);
        }).ToList();

        if (matchingMessages.Count == 0)
        {
            _logger.LogInformation("No emails from whitelisted senders in {Email}", mailbox.EmailAddress);
            scanState.LastScannedAt = DateTime.UtcNow;
            await _emailMonitoringRepository.UpsertScanStateAsync(scanState, cancellationToken);
            return;
        }

        _logger.LogInformation("Found {Count} emails from whitelisted senders in {Email}",
            matchingMessages.Count, mailbox.EmailAddress);

        foreach (var message in matchingMessages)
        {
            if (string.IsNullOrEmpty(message.Id))
                continue;

            try
            {
                var analysis = await AnalyzeEmailAsync(
                    message.Subject ?? "(no subject)",
                    message.From?.EmailAddress?.Address ?? "unknown",
                    message.BodyPreview ?? "",
                    cancellationToken);

                if (analysis.IsActionable)
                {
                    await NotifyFamilyAndCreatePendingAction(
                        mailbox.EmailAddress,
                        message.Id,
                        message.Subject ?? "(no subject)",
                        message.From?.EmailAddress?.Address ?? "unknown",
                        analysis,
                        cancellationToken);
                }
                else
                {
                    _logger.LogInformation("Email '{Subject}' is not actionable", message.Subject);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process email {EmailId} in {Mailbox}", message.Id, mailbox.EmailAddress);
            }
        }

        scanState.LastScannedAt = DateTime.UtcNow;
        scanState.LastProcessedEmailId = messages.Value.First().Id ?? "";
        await _emailMonitoringRepository.UpsertScanStateAsync(scanState, cancellationToken);
    }

    private static bool IsWhitelistedSender(string fromEmail, IReadOnlyList<WhitelistedSender> senders)
    {
        foreach (var sender in senders)
        {
            var pattern = sender.EmailAddress.Trim();

            // Domain match: @domain.com
            if (pattern.StartsWith('@'))
            {
                if (fromEmail.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // Exact email match
            else if (fromEmail.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal async Task<EmailAnalysisResult> AnalyzeEmailAsync(
        string subject,
        string fromAddress,
        string bodyPreview,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Claude");

        var request = new
        {
            model = ClaudeModel,
            max_tokens = 512,
            system = """
                You analyze emails to determine if they require action from a family.
                Respond ONLY with valid JSON — no markdown, no explanation.
                Use this exact format:
                {"isActionable": true/false, "summary": "brief summary", "suggestedActions": [{"type": "CalendarEvent" or "Reminder", "description": "what to do", "suggestedDate": "YYYY-MM-DD or null", "suggestedTime": "HH:mm or null"}]}
                An email is actionable if it contains dates, deadlines, events, appointments, or action items.
                Newsletters, marketing, and purely informational emails are NOT actionable.
                """,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = $"Analyze this email:\nFrom: {fromAddress}\nSubject: {subject}\nBody preview: {bodyPreview}"
                }
            }
        };

        try
        {
            var response = await client.PostAsJsonAsync(ClaudeApiUrl, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var claudeResponse = await response.Content.ReadFromJsonAsync<ClaudeAnalysisResponse>(cancellationToken: cancellationToken);
            var text = claudeResponse?.Content?.FirstOrDefault(b => b.Type == "text")?.Text;

            if (string.IsNullOrEmpty(text))
            {
                _logger.LogWarning("Claude returned empty analysis for email '{Subject}'", subject);
                return new EmailAnalysisResult { IsActionable = false };
            }

            var analysis = JsonSerializer.Deserialize<AnalysisResponse>(text);
            if (analysis == null)
            {
                return new EmailAnalysisResult { IsActionable = false };
            }

            return new EmailAnalysisResult
            {
                IsActionable = analysis.IsActionable,
                Summary = analysis.Summary,
                SuggestedActions = analysis.SuggestedActions.Select(a => new EmailSuggestedAction
                {
                    Type = a.Type,
                    Description = a.Description,
                    SuggestedDate = a.SuggestedDate,
                    SuggestedTime = a.SuggestedTime
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze email '{Subject}' via Claude", subject);
            return new EmailAnalysisResult { IsActionable = false };
        }
    }

    private async Task NotifyFamilyAndCreatePendingAction(
        string sourceMailbox,
        string emailId,
        string subject,
        string fromAddress,
        EmailAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        var pendingAction = new EmailActionPending
        {
            EmailId = emailId,
            EmailSubject = subject,
            SourceMailbox = sourceMailbox,
            SuggestedActions = analysis.SuggestedActions,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        await _emailMonitoringRepository.UpsertActionAsync(pendingAction, cancellationToken);

        var message = FormatNotificationMessage(subject, fromAddress, analysis);

        var members = await _familyMemberRepository.GetAllAsync(cancellationToken);

        var centralTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var centralNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, centralTimeZone);

        foreach (var member in members)
        {
            try
            {
                if (centralNow.Hour >= 22 || centralNow.Hour < 7)
                {
                    // Quiet hours — schedule a 7am reminder
                    var tomorrow7am = centralNow.Date.AddDays(centralNow.Hour >= 22 ? 1 : 0).AddHours(7);
                    var fireAt = new DateTimeOffset(tomorrow7am, centralTimeZone.GetUtcOffset(tomorrow7am));

                    await _reminderService.CreateReminderAsync(
                        member.PhoneNumber,
                        message,
                        fireAt,
                        cancellationToken);

                    _logger.LogInformation("Scheduled email notification for {Member} at 7am (quiet hours)",
                        member.Name);
                }
                else
                {
                    await _whatsAppService.SendMessageAsync(member.PhoneNumber, message, cancellationToken);
                    _logger.LogInformation("Sent email notification to {Member}", member.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify {Member} about email '{Subject}'",
                    member.Name, subject);
            }
        }
    }

    private static string FormatNotificationMessage(
        string subject,
        string fromAddress,
        EmailAnalysisResult analysis)
    {
        var msg = $"📧 New actionable email detected!\n\n";
        msg += $"From: {fromAddress}\n";
        msg += $"Subject: {subject}\n\n";
        msg += $"Summary: {analysis.Summary}\n\n";

        if (analysis.SuggestedActions.Count == 1)
        {
            var action = analysis.SuggestedActions[0];
            msg += $"Suggested action: {action.Description}";
            if (!string.IsNullOrEmpty(action.SuggestedDate))
                msg += $" ({action.SuggestedDate}";
            if (!string.IsNullOrEmpty(action.SuggestedTime))
                msg += $" at {action.SuggestedTime}";
            if (!string.IsNullOrEmpty(action.SuggestedDate))
                msg += ")";
            msg += "\n\nReply *yes* to create or *no* to skip.";
        }
        else if (analysis.SuggestedActions.Count > 1)
        {
            msg += "Suggested actions:\n";
            for (var i = 0; i < analysis.SuggestedActions.Count; i++)
            {
                var action = analysis.SuggestedActions[i];
                msg += $"{i + 1}. {action.Description}";
                if (!string.IsNullOrEmpty(action.SuggestedDate))
                    msg += $" ({action.SuggestedDate}";
                if (!string.IsNullOrEmpty(action.SuggestedTime))
                    msg += $" at {action.SuggestedTime}";
                if (!string.IsNullOrEmpty(action.SuggestedDate))
                    msg += ")";
                msg += "\n";
            }
            msg += "\nReply with the number of the action to take, or *no* to skip.";
        }

        return msg;
    }
}

file class ClaudeAnalysisResponse
{
    [JsonPropertyName("content")] public List<ClaudeContentBlock>? Content { get; set; }
}

file class ClaudeContentBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("text")] public string? Text { get; set; }
}

file class AnalysisResponse
{
    [JsonPropertyName("isActionable")] public bool IsActionable { get; set; }
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("suggestedActions")] public List<AnalysisAction> SuggestedActions { get; set; } = new();
}

file class AnalysisAction
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("suggestedDate")] public string? SuggestedDate { get; set; }
    [JsonPropertyName("suggestedTime")] public string? SuggestedTime { get; set; }
}
