using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BigDamnAssistant.Core.Services;

public class ClaudeService : IClaudeService
{
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const int MaxToolRounds = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICalendarService _calendarService;
    private readonly IMailService _mailService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IFamilyMemberRepository _familyMemberRepository;
    private readonly IFamilyMemoryRepository _familyMemoryRepository;
    private readonly IEmailMonitoringRepository _emailMonitoringRepository;
    private readonly IMemberPreferencesRepository _memberPreferencesRepository;
    private readonly IFamilyProfileRepository _familyProfileRepository;
    private readonly IKidSmsRepository _kidSmsRepository;
    private readonly IFunContentService _funContentService;
    private readonly IAffirmationRepository _affirmationRepository;
    private readonly IFeatureRequestRepository _featureRequestRepository;
    private readonly ILogger<ClaudeService> _logger;
    private readonly AssistantOptions _assistantOptions;

    // Track the requesting member and conversation for tool handlers
    private FamilyMember? _currentMember;
    private ConversationHistory? _currentConversation;

    public ClaudeService(
        IHttpClientFactory httpClientFactory,
        ICalendarService calendarService,
        IMailService mailService,
        IWhatsAppService whatsAppService,
        IFamilyMemberRepository familyMemberRepository,
        IFamilyMemoryRepository familyMemoryRepository,
        IEmailMonitoringRepository emailMonitoringRepository,
        IMemberPreferencesRepository memberPreferencesRepository,
        IFamilyProfileRepository familyProfileRepository,
        IKidSmsRepository kidSmsRepository,
        IFunContentService funContentService,
        IAffirmationRepository affirmationRepository,
        IFeatureRequestRepository featureRequestRepository,
        ILogger<ClaudeService> logger,
        IOptions<AssistantOptions> assistantOptions)
    {
        _httpClientFactory = httpClientFactory;
        _calendarService = calendarService;
        _mailService = mailService;
        _whatsAppService = whatsAppService;
        _familyMemberRepository = familyMemberRepository;
        _familyMemoryRepository = familyMemoryRepository;
        _emailMonitoringRepository = emailMonitoringRepository;
        _memberPreferencesRepository = memberPreferencesRepository;
        _familyProfileRepository = familyProfileRepository;
        _kidSmsRepository = kidSmsRepository;
        _funContentService = funContentService;
        _affirmationRepository = affirmationRepository;
        _featureRequestRepository = featureRequestRepository;
        _logger = logger;
        _assistantOptions = assistantOptions.Value;
    }

    public async Task<string> GetResponseAsync(
        FamilyMember member,
        ConversationHistory history,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _currentMember = member;
        _currentConversation = history;
        var client = _httpClientFactory.CreateClient("Claude");
        var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, member.Timezone);

        // Load family memories, member preferences, and family profiles for system prompt injection
        var memories = await _familyMemoryRepository.GetAllAsync(cancellationToken);
        var preferences = await _memberPreferencesRepository.GetAsync(member.PhoneNumber, cancellationToken);
        var profiles = await _familyProfileRepository.GetAllActiveAsync(cancellationToken);
        var kidContacts = await _kidSmsRepository.GetAllActiveAsync(cancellationToken);
        var systemPrompt = BuildSystemPrompt(member, now, memories, preferences, history.SessionSummaries, profiles, kidContacts);
        var messages = BuildMessages(history, userMessage);
        var tools = BuildToolDefinitions();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var request = new ClaudeRequest
            {
                Model = Model,
                MaxTokens = 4096,
                System = systemPrompt,
                Messages = messages,
                Tools = tools
            };

            try
            {
                var response = await client.PostAsJsonAsync(ApiUrl, request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: cancellationToken);

                if (result?.StopReason == "tool_use" && result.Content != null)
                {
                    messages.Add(new ClaudeMessage { Role = "assistant", Content = result.Content });

                    var toolResults = new List<object>();
                    foreach (var block in result.Content.Where(b => b.Type == "tool_use"))
                    {
                        _logger.LogInformation("Claude calling tool: {ToolName} (id: {ToolId})", block.Name, block.Id);
                        var toolResult = await ExecuteToolAsync(block.Name!, block.Id!, block.Input, member.Timezone, cancellationToken);
                        toolResults.Add(toolResult);
                    }

                    messages.Add(new ClaudeMessage { Role = "user", Content = toolResults });
                    continue;
                }

                var text = string.Join("", result?.Content?.Where(b => b.Type == "text").Select(b => b.Text) ?? []);

                if (string.IsNullOrEmpty(text))
                {
                    _logger.LogWarning("Claude returned empty response for member {MemberId}", member.Id);
                    return "Sorry, I didn't get a response. Can you try again?";
                }

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude API call failed for member {MemberId}", member.Id);
                return "I'm having trouble thinking right now. Give me a moment and try again.";
            }
        }

        _logger.LogWarning("Claude exceeded max tool rounds for member {MemberId}", member.Id);
        return "I got a bit lost working on that. Can you try asking again?";
    }

    public async Task<string> GetKidResponseAsync(
        KidSmsUser kid,
        FamilyProfile profile,
        ConversationHistory history,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _currentMember = null;
        _currentConversation = history;
        var client = _httpClientFactory.CreateClient("Claude");
        var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "America/Chicago");

        var systemPrompt = BuildKidSystemPrompt(kid, profile, now);
        var messages = BuildMessages(history, userMessage);
        var tools = BuildKidToolDefinitions();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var request = new ClaudeRequest
            {
                Model = Model,
                MaxTokens = 256,
                System = systemPrompt,
                Messages = messages,
                Tools = tools
            };

            try
            {
                var response = await client.PostAsJsonAsync(ApiUrl, request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: cancellationToken);

                if (result?.StopReason == "tool_use" && result.Content != null)
                {
                    messages.Add(new ClaudeMessage { Role = "assistant", Content = result.Content });

                    var toolResults = new List<object>();
                    foreach (var block in result.Content.Where(b => b.Type == "tool_use"))
                    {
                        _logger.LogInformation("Kid Claude calling tool: {ToolName}", block.Name);
                        var toolResult = await ExecuteToolAsync(block.Name!, block.Id!, block.Input, "America/Chicago", cancellationToken);
                        toolResults.Add(toolResult);
                    }

                    messages.Add(new ClaudeMessage { Role = "user", Content = toolResults });
                    continue;
                }

                var text = string.Join("", result?.Content?.Where(b => b.Type == "text").Select(b => b.Text) ?? []);

                if (string.IsNullOrEmpty(text))
                    return "Hmm, I got stuck. Try asking again!";

                return TruncateToSentences(text, 3);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude API call failed for kid {KidName}", kid.DisplayName);
                return "Oops, I got confused. Try again in a sec!";
            }
        }

        return "I got a bit lost. Can you ask me again?";
    }

    private static string TruncateToSentences(string text, int maxSentences)
    {
        var sentences = text.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length <= maxSentences)
            return text;

        var result = string.Join(". ", sentences.Take(maxSentences));
        if (!result.EndsWith('.') && !result.EndsWith('!') && !result.EndsWith('?'))
            result += ".";
        return result;
    }

    private async Task<object> ExecuteToolAsync(string toolName, string toolId, JsonElement? input, string memberTimezone, CancellationToken cancellationToken)
    {
        try
        {
            var result = toolName switch
            {
                "get_calendar_events" => await HandleGetCalendarEvents(input, memberTimezone, cancellationToken),
                "create_calendar_event" => await HandleCreateCalendarEvent(input, cancellationToken),
                "cancel_calendar_event" => await HandleCancelCalendarEvent(input, cancellationToken),
                "get_family_members" => await HandleGetFamilyMembers(cancellationToken),
                "save_memory" => await HandleSaveMemory(input, cancellationToken),
                "delete_memory" => await HandleDeleteMemory(input, cancellationToken),
                "list_memories" => await HandleListMemories(cancellationToken),
                "send_email" => await HandleSendEmail(input, cancellationToken),
                "add_monitored_mailbox" => await HandleAddMonitoredMailbox(input, cancellationToken),
                "remove_monitored_mailbox" => await HandleRemoveMonitoredMailbox(input, cancellationToken),
                "list_monitored_mailboxes" => await HandleListMonitoredMailboxes(cancellationToken),
                "add_whitelisted_sender" => await HandleAddWhitelistedSender(input, cancellationToken),
                "remove_whitelisted_sender" => await HandleRemoveWhitelistedSender(input, cancellationToken),
                "list_whitelisted_senders" => await HandleListWhitelistedSenders(cancellationToken),
                "send_joke" => await HandleSendJoke(input, cancellationToken),
                "send_fun_fact" => await HandleSendFunFact(input, cancellationToken),
                "add_affirmation" => await HandleAddAffirmation(input, cancellationToken),
                "remove_affirmation" => await HandleRemoveAffirmation(input, cancellationToken),
                "list_affirmations" => await HandleListAffirmations(cancellationToken),
                "add_feature_request" => await HandleAddFeatureRequest(input, cancellationToken),
                "remove_feature_request" => await HandleRemoveFeatureRequest(input, cancellationToken),
                "list_feature_requests" => await HandleListFeatureRequests(cancellationToken),
                "clear_conversation" => HandleClearConversation(),
                "set_preference" => await HandleSetPreference(input, cancellationToken),
                "list_preferences" => await HandleListPreferences(cancellationToken),
                "remove_preference" => await HandleRemovePreference(input, cancellationToken),
                "create_family_profile" => await HandleCreateFamilyProfile(input, cancellationToken),
                "update_family_profile" => await HandleUpdateFamilyProfile(input, cancellationToken),
                "view_family_profile" => await HandleViewFamilyProfile(input, cancellationToken),
                "list_family_profiles" => await HandleListFamilyProfiles(cancellationToken),
                "deactivate_family_profile" => await HandleDeactivateFamilyProfile(input, cancellationToken),
                "register_kid_contact" => await HandleRegisterKidContact(input, cancellationToken),
                "update_kid_channel" => await HandleUpdateKidChannel(input, cancellationToken),
                "send_kid_alert" => await HandleSendKidAlert(input, cancellationToken),
                _ => $"Unknown tool: {toolName}"
            };

            _logger.LogInformation("Tool {ToolName} completed successfully", toolName);
            return new { type = "tool_result", tool_use_id = toolId, content = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} failed: {Message}", toolName, ex.Message);
            return new { type = "tool_result", tool_use_id = toolId, content = $"Error: {ex.Message}", is_error = true };
        }
    }

    #region Calendar Tools

    private async Task<string> HandleGetCalendarEvents(JsonElement? input, string memberTimezone, CancellationToken cancellationToken)
    {
        var fromStr = input?.GetProperty("from").GetString();
        var toStr = input?.GetProperty("to").GetString();

        if (string.IsNullOrEmpty(fromStr) || string.IsNullOrEmpty(toStr))
            return "Error: 'from' and 'to' parameters are required.";

        var from = DateTimeOffset.Parse(fromStr);
        var to = DateTimeOffset.Parse(toStr);

        _logger.LogInformation("Fetching calendar events from {From} to {To}", from, to);

        var events = await _calendarService.GetEventsAsync(from, to, cancellationToken);

        _logger.LogInformation("Calendar returned {Count} events between {From} and {To}", events.Count, from, to);
        foreach (var evt in events)
        {
            _logger.LogInformation("  Event: '{Subject}' at {Start}", evt.Subject, evt.Start);
        }

        if (events.Count == 0)
            return "No events found in the specified date range.";

        var tz = TimeZoneInfo.FindSystemTimeZoneById(memberTimezone);
        var eventList = events.Select(e =>
        {
            var localStart = TimeZoneInfo.ConvertTime(e.Start, tz);
            var localEnd = TimeZoneInfo.ConvertTime(e.End, tz);
            return new
            {
                id = e.Id,
                subject = e.Subject,
                start = localStart.ToString("dddd, yyyy-MM-dd h:mm tt"),
                end = localEnd.ToString("dddd, yyyy-MM-dd h:mm tt"),
                location = e.Location ?? "No location",
                attendees = e.Attendees
            };
        });

        return JsonSerializer.Serialize(eventList);
    }

    private async Task<string> HandleCreateCalendarEvent(JsonElement? input, CancellationToken cancellationToken)
    {
        var subject = input?.GetProperty("subject").GetString() ?? "";
        var startStr = input?.GetProperty("start").GetString() ?? "";
        var endStr = input?.GetProperty("end").GetString() ?? "";
        var location = input?.TryGetProperty("location", out var loc) == true ? loc.GetString() : null;

        var attendees = new List<string>();
        if (input?.TryGetProperty("attendees", out var attendeesEl) == true && attendeesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var email in attendeesEl.EnumerateArray())
            {
                var addr = email.GetString();
                if (!string.IsNullOrEmpty(addr))
                    attendees.Add(addr);
            }
        }

        var calendarEvent = new CalendarEvent
        {
            Subject = subject,
            Start = DateTimeOffset.Parse(startStr),
            End = DateTimeOffset.Parse(endStr),
            Location = location,
            Attendees = attendees
        };

        _logger.LogInformation("Creating calendar event: {Subject} at {Start} with {AttendeeCount} attendees", subject, startStr, attendees.Count);

        var created = await _calendarService.CreateEventAsync(calendarEvent, cancellationToken);

        var result = $"Event '{created.Subject}' created successfully.";
        if (attendees.Count > 0)
            result += $" Invitations sent to: {string.Join(", ", attendees)}";
        return result;
    }

    private async Task<string> HandleCancelCalendarEvent(JsonElement? input, CancellationToken cancellationToken)
    {
        var eventId = input?.GetProperty("event_id").GetString();

        if (string.IsNullOrEmpty(eventId))
            return "Error: 'event_id' is required. Use get_calendar_events first to find the event ID.";

        _logger.LogInformation("Cancelling calendar event {EventId}", eventId);

        await _calendarService.CancelEventAsync(eventId, cancellationToken);
        return "Event cancelled successfully.";
    }

    #endregion

    #region Family Member Tools

    private async Task<string> HandleGetFamilyMembers(CancellationToken cancellationToken)
    {
        var members = await _familyMemberRepository.GetAllAsync(cancellationToken);

        var memberList = members.Select(m => new
        {
            name = m.Name,
            email = m.Email,
            phone = m.PhoneNumber
        });

        return JsonSerializer.Serialize(memberList);
    }

    #endregion

    #region Memory Tools

    private async Task<string> HandleSaveMemory(JsonElement? input, CancellationToken cancellationToken)
    {
        var key = input?.GetProperty("key").GetString()?.Trim().ToLowerInvariant() ?? "";
        var value = input?.GetProperty("value").GetString() ?? "";

        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            return "Error: 'key' and 'value' are required.";

        // Check for sensitive content
        if (ContainsSensitiveData(value))
            return "I'd rather not store sensitive financial or security details like full card numbers or SSNs. Is there something else I can help you remember?";

        // Check for existing memory with same key for deduplication
        var existing = await _familyMemoryRepository.GetAllAsync(cancellationToken);
        var match = existing.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            match.Value = value;
            match.UpdatedAt = DateTime.UtcNow;
            await _familyMemoryRepository.UpsertAsync(match, cancellationToken);
            _logger.LogInformation("Updated family memory: {Key}", key);
            return $"Updated existing memory: {value}";
        }

        var memory = new FamilyMemory
        {
            Key = key,
            Value = value,
            CreatedBy = _currentMember?.PhoneNumber ?? "",
            CreatedByName = _currentMember?.Name ?? "Unknown"
        };

        await _familyMemoryRepository.UpsertAsync(memory, cancellationToken);
        _logger.LogInformation("Saved new family memory: {Key}", key);
        return $"Saved: {value}";
    }

    private async Task<string> HandleDeleteMemory(JsonElement? input, CancellationToken cancellationToken)
    {
        var key = input?.GetProperty("key").GetString()?.Trim().ToLowerInvariant() ?? "";

        if (string.IsNullOrEmpty(key))
            return "Error: 'key' is required.";

        var existing = await _familyMemoryRepository.GetAllAsync(cancellationToken);
        var match = existing.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return $"No memory found matching '{key}'.";

        var deleted = await _familyMemoryRepository.DeleteAsync(match.Id, cancellationToken);
        _logger.LogInformation("Deleted family memory: {Key}", key);
        return deleted ? $"Forgotten: {match.Value}" : $"Failed to delete memory '{key}'.";
    }

    private async Task<string> HandleListMemories(CancellationToken cancellationToken)
    {
        var memories = await _familyMemoryRepository.GetAllAsync(cancellationToken);

        if (memories.Count == 0)
            return "No family memories stored yet.";

        var list = memories.Select(m => new
        {
            id = m.Id,
            key = m.Key,
            value = m.Value,
            savedBy = m.CreatedByName
        });

        return JsonSerializer.Serialize(list);
    }

    private static bool ContainsSensitiveData(string value)
    {
        var lower = value.ToLowerInvariant();
        // Check for credit card patterns (16 digits) or SSN patterns (XXX-XX-XXXX)
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"\b\d{3}-\d{2}-\d{4}\b"))
            return true;
        return false;
    }

    #endregion

    #region Email Tools

    private async Task<string> HandleSendEmail(JsonElement? input, CancellationToken cancellationToken)
    {
        var to = input?.GetProperty("to").GetString() ?? "";
        var subject = input?.GetProperty("subject").GetString() ?? "";
        var emailBody = input?.GetProperty("body").GetString() ?? "";

        _logger.LogInformation("Sending email to {To} with subject '{Subject}'", to, subject);

        await _mailService.SendMailAsync(to, subject, emailBody, cancellationToken);
        return $"Email sent to {to} with subject '{subject}'.";
    }

    #endregion

    #region Email Monitoring Tools

    private async Task<string> HandleAddMonitoredMailbox(JsonElement? input, CancellationToken cancellationToken)
    {
        var emailAddress = input?.GetProperty("email_address").GetString() ?? "";
        var displayName = input?.GetProperty("display_name").GetString() ?? "";

        if (string.IsNullOrEmpty(emailAddress))
            return "Error: 'email_address' is required.";

        var mailbox = new Models.MonitoredMailbox
        {
            EmailAddress = emailAddress,
            DisplayName = displayName,
            AddedBy = _currentMember?.PhoneNumber ?? ""
        };

        await _emailMonitoringRepository.UpsertMailboxAsync(mailbox, cancellationToken);
        _logger.LogInformation("Added monitored mailbox: {Email}", emailAddress);
        return $"Now monitoring mailbox: {displayName} ({emailAddress})";
    }

    private async Task<string> HandleRemoveMonitoredMailbox(JsonElement? input, CancellationToken cancellationToken)
    {
        var emailAddress = input?.GetProperty("email_address").GetString() ?? "";

        if (string.IsNullOrEmpty(emailAddress))
            return "Error: 'email_address' is required.";

        var removed = await _emailMonitoringRepository.RemoveMailboxAsync(emailAddress, cancellationToken);
        _logger.LogInformation("Removed monitored mailbox: {Email} (found={Found})", emailAddress, removed);
        return removed
            ? $"Stopped monitoring mailbox: {emailAddress}"
            : $"No active monitored mailbox found for {emailAddress}";
    }

    private async Task<string> HandleListMonitoredMailboxes(CancellationToken cancellationToken)
    {
        var mailboxes = await _emailMonitoringRepository.GetActiveMailboxesAsync(cancellationToken);

        if (mailboxes.Count == 0)
            return "No monitored mailboxes configured.";

        var list = mailboxes.Select(m => new
        {
            emailAddress = m.EmailAddress,
            displayName = m.DisplayName,
            addedAt = m.AddedAt.ToString("yyyy-MM-dd")
        });

        return JsonSerializer.Serialize(list);
    }

    private async Task<string> HandleAddWhitelistedSender(JsonElement? input, CancellationToken cancellationToken)
    {
        var emailAddress = input?.GetProperty("email_address").GetString() ?? "";
        var displayName = input?.TryGetProperty("display_name", out var dn) == true ? dn.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(emailAddress))
            return "Error: 'email_address' is required.";

        var sender = new Models.WhitelistedSender
        {
            EmailAddress = emailAddress,
            DisplayName = displayName,
            AddedBy = _currentMember?.PhoneNumber ?? ""
        };

        await _emailMonitoringRepository.UpsertSenderAsync(sender, cancellationToken);
        _logger.LogInformation("Added whitelisted sender: {Email}", emailAddress);
        return $"Added whitelisted sender: {(string.IsNullOrEmpty(displayName) ? emailAddress : $"{displayName} ({emailAddress})")}";
    }

    private async Task<string> HandleRemoveWhitelistedSender(JsonElement? input, CancellationToken cancellationToken)
    {
        var emailAddress = input?.GetProperty("email_address").GetString() ?? "";

        if (string.IsNullOrEmpty(emailAddress))
            return "Error: 'email_address' is required.";

        var removed = await _emailMonitoringRepository.RemoveSenderAsync(emailAddress, cancellationToken);
        _logger.LogInformation("Removed whitelisted sender: {Email} (found={Found})", emailAddress, removed);
        return removed
            ? $"Removed whitelisted sender: {emailAddress}"
            : $"No active whitelisted sender found for {emailAddress}";
    }

    private async Task<string> HandleListWhitelistedSenders(CancellationToken cancellationToken)
    {
        var senders = await _emailMonitoringRepository.GetActiveSendersAsync(cancellationToken);

        if (senders.Count == 0)
            return "No whitelisted senders configured.";

        var list = senders.Select(s => new
        {
            emailAddress = s.EmailAddress,
            displayName = s.DisplayName,
            addedAt = s.AddedAt.ToString("yyyy-MM-dd")
        });

        return JsonSerializer.Serialize(list);
    }

    #endregion

    #region Fun Content Tools

    private async Task<string> HandleSendJoke(JsonElement? input, CancellationToken cancellationToken)
    {
        var targetName = input?.GetProperty("target_name").GetString() ?? "";
        return await SendFunContent(targetName, "joke", cancellationToken);
    }

    private async Task<string> HandleSendFunFact(JsonElement? input, CancellationToken cancellationToken)
    {
        var targetName = input?.GetProperty("target_name").GetString() ?? "";
        var topic = input?.TryGetProperty("topic", out var t) == true ? t.GetString() : null;
        return await SendFunContent(targetName, "fact", cancellationToken, topic);
    }

    private async Task<string> SendFunContent(string targetName, string contentType, CancellationToken cancellationToken, string? topic = null)
    {
        if (string.IsNullOrEmpty(targetName))
            return "Error: 'target_name' is required.";

        var members = await _familyMemberRepository.GetAllAsync(cancellationToken);
        var target = members.FirstOrDefault(m =>
            m.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
            m.Nicknames.Any(n => n.Equals(targetName, StringComparison.OrdinalIgnoreCase)));

        if (target == null)
            return $"I don't know who {targetName} is — make sure they're set up as a family member.";

        if (target.PhoneNumber == _currentMember?.PhoneNumber)
            return "You can't send a joke to yourself — pick someone else! 😄";

        if (string.IsNullOrEmpty(target.PhoneNumber))
            return $"I don't have a WhatsApp number for {target.Name} — ask them to join BDA first.";

        string content;
        string emoji;
        string label;
        try
        {
            if (contentType == "joke")
            {
                content = await _funContentService.GenerateJokeAsync(cancellationToken);
                emoji = "😄";
                label = "a joke";
            }
            else
            {
                content = await _funContentService.GenerateFactAsync(topic, cancellationToken);
                emoji = "🧠";
                label = string.IsNullOrEmpty(topic) ? "a fun fact" : $"a {topic} fact";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate fun content");
            return "I couldn't think of anything right now — try again in a moment!";
        }

        var senderName = _currentMember?.Name ?? "Someone";
        var recipientMessage = contentType == "joke"
            ? $"Hey {target.Name}! {senderName} thought you could use a laugh {emoji}\n\n{content}"
            : $"Hey {target.Name}! {senderName} sent you a fun fact {emoji}\n\n{content}";

        try
        {
            await _whatsAppService.SendMessageAsync(target.PhoneNumber, recipientMessage, cancellationToken);
            _logger.LogInformation("Sent {ContentType} from {Sender} to {Recipient}", contentType, senderName, target.Name);
            return $"Sent {target.Name} {label} {emoji}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {ContentType} to {Recipient}", contentType, target.Name);
            return $"I generated the content but couldn't deliver it to {target.Name}. Try again.";
        }
    }

    #endregion

    #region Affirmation Tools

    private async Task<string> HandleAddAffirmation(JsonElement? input, CancellationToken cancellationToken)
    {
        var text = input?.GetProperty("text").GetString() ?? "";

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' is required.";

        var affirmation = new AffirmationPoolItem
        {
            Text = text,
            AddedBy = _currentMember?.PhoneNumber ?? "",
            AddedByName = _currentMember?.Name ?? "Unknown"
        };

        await _affirmationRepository.UpsertAffirmationAsync(affirmation, cancellationToken);
        _logger.LogInformation("Added affirmation to pool by {MemberName}", _currentMember?.Name);
        return $"Added affirmation to the shared pool: \"{text}\"";
    }

    private async Task<string> HandleRemoveAffirmation(JsonElement? input, CancellationToken cancellationToken)
    {
        var text = input?.GetProperty("text").GetString() ?? "";

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' is required.";

        var removed = await _affirmationRepository.RemoveAffirmationAsync(text, cancellationToken);
        _logger.LogInformation("Remove affirmation requested: '{Text}' (found={Found})", text, removed);
        return removed
            ? $"Removed affirmation from the pool."
            : $"No active affirmation found matching that text.";
    }

    private async Task<string> HandleListAffirmations(CancellationToken cancellationToken)
    {
        var affirmations = await _affirmationRepository.GetActiveAffirmationsAsync(cancellationToken);

        if (affirmations.Count == 0)
            return "No affirmations in the shared pool yet.";

        var list = affirmations.Select(a => new
        {
            text = a.Text,
            addedByName = a.AddedByName,
            usedCount = a.UsedCount
        });

        return JsonSerializer.Serialize(list);
    }

    #endregion

    #region Feature Request Tools

    private async Task<string> HandleAddFeatureRequest(JsonElement? input, CancellationToken cancellationToken)
    {
        var description = input?.GetProperty("description").GetString() ?? "";

        if (string.IsNullOrEmpty(description))
            return "Error: 'description' is required.";

        var request = new Models.FeatureRequest
        {
            Description = description,
            RequestedBy = _currentMember?.PhoneNumber ?? "",
            RequestedByName = _currentMember?.Name ?? "Unknown"
        };

        await _featureRequestRepository.AddRequestAsync(request, cancellationToken);
        var all = await _featureRequestRepository.GetActiveRequestsAsync(cancellationToken);
        _logger.LogInformation("Added feature request: {Description}", description);
        return JsonSerializer.Serialize(new { added = description, totalCount = all.Count });
    }

    private async Task<string> HandleRemoveFeatureRequest(JsonElement? input, CancellationToken cancellationToken)
    {
        var description = input?.GetProperty("description").GetString() ?? "";

        if (string.IsNullOrEmpty(description))
            return "Error: 'description' is required.";

        // Find best match from active requests
        var requests = await _featureRequestRepository.GetActiveRequestsAsync(cancellationToken);
        var match = requests.FirstOrDefault(r =>
            r.Description.Contains(description, StringComparison.OrdinalIgnoreCase) ||
            description.Contains(r.Description, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return $"No feature request found matching '{description}'. Use list_feature_requests to see current requests.";

        var removed = await _featureRequestRepository.RemoveRequestAsync(match.Id, cancellationToken);
        _logger.LogInformation("Removed feature request: {Description}", match.Description);
        return removed ? $"Removed: {match.Description}" : "Failed to remove the feature request.";
    }

    private async Task<string> HandleListFeatureRequests(CancellationToken cancellationToken)
    {
        var requests = await _featureRequestRepository.GetActiveRequestsAsync(cancellationToken);

        if (requests.Count == 0)
            return "No feature requests yet.";

        var list = requests.OrderBy(r => r.RequestedAt).Select(r => new
        {
            description = r.Description,
            requestedByName = r.RequestedByName,
            requestedAt = r.RequestedAt.ToString("yyyy-MM-dd")
        });

        return JsonSerializer.Serialize(list);
    }

    #endregion

    #region Conversation Tools

    private string HandleClearConversation()
    {
        if (_currentConversation != null)
        {
            _currentConversation.CurrentSessionMessages.Clear();
            _currentConversation.SessionSummaries.Clear();
            _currentConversation.PendingAction = null;
            _logger.LogInformation("Conversation history cleared for {Phone}", _currentConversation.PhoneNumber);
        }

        return "Conversation history cleared.";
    }

    #endregion

    #region Preference Tools

    private async Task<string> HandleSetPreference(JsonElement? input, CancellationToken cancellationToken)
    {
        if (_currentMember == null)
            return "Error: no current member context.";

        var field = input?.TryGetProperty("field", out var f) == true ? f.GetString() : null;
        var value = input?.TryGetProperty("value", out var v) == true ? v.GetString() : null;
        var learnedKey = input?.TryGetProperty("learned_key", out var lk) == true ? lk.GetString() : null;
        var learnedValue = input?.TryGetProperty("learned_value", out var lv) == true ? lv.GetString() : null;

        if (string.IsNullOrEmpty(field) && string.IsNullOrEmpty(learnedKey))
            return "Error: either 'field' or 'learned_key' is required.";

        var prefs = await _memberPreferencesRepository.GetAsync(_currentMember.PhoneNumber, cancellationToken)
            ?? new MemberPreferences
            {
                Id = $"prefs-{_currentMember.PhoneNumber}",
                PhoneNumber = _currentMember.PhoneNumber,
                MemberName = _currentMember.Name
            };

        if (!string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(value))
        {
            switch (field.ToLowerInvariant())
            {
                case "briefinglength":
                    prefs.BriefingLength = value;
                    break;
                case "communicationstyle":
                    prefs.CommunicationStyle = value;
                    break;
                case "quiethoursstart":
                    prefs.QuietHoursStart = value;
                    break;
                case "quiethoursend":
                    prefs.QuietHoursEnd = value;
                    break;
                case "defaultreminderleadtimehours":
                    if (int.TryParse(value, out var hours))
                        prefs.DefaultReminderLeadTimeHours = hours;
                    break;
                default:
                    prefs.LearnedPreferences[field] = value;
                    break;
            }
        }

        if (!string.IsNullOrEmpty(learnedKey) && !string.IsNullOrEmpty(learnedValue))
        {
            prefs.LearnedPreferences[learnedKey] = learnedValue;
        }

        prefs.UpdatedAt = DateTime.UtcNow;
        await _memberPreferencesRepository.UpsertAsync(prefs, cancellationToken);

        _logger.LogInformation("Preference updated for {MemberName}: {Field}/{Key}", _currentMember.Name, field ?? learnedKey, value ?? learnedValue);
        return "Preference saved successfully.";
    }

    private async Task<string> HandleListPreferences(CancellationToken cancellationToken)
    {
        if (_currentMember == null)
            return "Error: no current member context.";

        var prefs = await _memberPreferencesRepository.GetAsync(_currentMember.PhoneNumber, cancellationToken);
        if (prefs == null)
            return "No preferences have been set yet.";

        return JsonSerializer.Serialize(new
        {
            communicationStyle = prefs.CommunicationStyle,
            briefingLength = prefs.BriefingLength,
            quietHoursStart = prefs.QuietHoursStart,
            quietHoursEnd = prefs.QuietHoursEnd,
            defaultReminderLeadTimeHours = prefs.DefaultReminderLeadTimeHours,
            topicsOfInterest = prefs.TopicsOfInterest,
            topicsToAvoid = prefs.TopicsToAvoid,
            learnedPreferences = prefs.LearnedPreferences
        });
    }

    private async Task<string> HandleRemovePreference(JsonElement? input, CancellationToken cancellationToken)
    {
        if (_currentMember == null)
            return "Error: no current member context.";

        var key = input?.TryGetProperty("key", out var k) == true ? k.GetString() : null;
        if (string.IsNullOrEmpty(key))
            return "Error: 'key' is required.";

        var prefs = await _memberPreferencesRepository.GetAsync(_currentMember.PhoneNumber, cancellationToken);
        if (prefs == null)
            return "No preferences found.";

        var removed = false;
        var lowerKey = key.ToLowerInvariant();

        // Check structured fields — reset to defaults
        switch (lowerKey)
        {
            case "briefinglength":
                prefs.BriefingLength = "normal";
                removed = true;
                break;
            case "communicationstyle":
                prefs.CommunicationStyle = "casual";
                removed = true;
                break;
            case "quiethoursstart":
                prefs.QuietHoursStart = null;
                removed = true;
                break;
            case "quiethoursend":
                prefs.QuietHoursEnd = null;
                removed = true;
                break;
            case "defaultreminderleadtimehours":
                prefs.DefaultReminderLeadTimeHours = 24;
                removed = true;
                break;
        }

        // Check learned preferences
        if (!removed && prefs.LearnedPreferences.Remove(key))
        {
            removed = true;
        }

        // Try case-insensitive match on learned preferences
        if (!removed)
        {
            var match = prefs.LearnedPreferences.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                prefs.LearnedPreferences.Remove(match);
                removed = true;
            }
        }

        if (!removed)
            return $"No preference found matching '{key}'.";

        prefs.UpdatedAt = DateTime.UtcNow;
        await _memberPreferencesRepository.UpsertAsync(prefs, cancellationToken);
        return $"Preference '{key}' removed successfully.";
    }

    #endregion

    #region Family Profile Tools

    private async Task<string> HandleCreateFamilyProfile(JsonElement? input, CancellationToken cancellationToken)
    {
        var name = input?.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrEmpty(name)) return "Error: 'name' is required.";

        var existing = await _familyProfileRepository.GetByNameAsync(name, cancellationToken);
        if (existing != null) return $"A profile for '{name}' already exists. Use update_family_profile to modify it.";

        var profile = new FamilyProfile
        {
            Name = name,
            AddedBy = _currentMember?.PhoneNumber ?? ""
        };

        if (input?.TryGetProperty("age", out var age) == true) profile.Age = age.GetInt32();
        if (input?.TryGetProperty("profile_type", out var pt) == true) profile.ProfileType = pt.GetString() ?? "child";
        if (input?.TryGetProperty("date_of_birth", out var dob) == true) profile.DateOfBirth = dob.GetString();
        if (input?.TryGetProperty("school", out var school) == true) profile.School = school.GetString() ?? "";
        if (input?.TryGetProperty("grade", out var grade) == true) profile.Grade = grade.GetString() ?? "";

        await _familyProfileRepository.UpsertAsync(profile, cancellationToken);
        _logger.LogInformation("Created family profile for {Name}", name);
        return $"Created profile for {name}.";
    }

    private async Task<string> HandleUpdateFamilyProfile(JsonElement? input, CancellationToken cancellationToken)
    {
        var name = input?.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrEmpty(name)) return "Error: 'name' is required.";

        var profile = await _familyProfileRepository.GetByNameAsync(name, cancellationToken);
        if (profile == null) return $"No profile found for '{name}'. Create one first with create_family_profile.";

        if (input?.TryGetProperty("age", out var age) == true) profile.Age = age.GetInt32();
        if (input?.TryGetProperty("school", out var school) == true) profile.School = school.GetString() ?? "";
        if (input?.TryGetProperty("grade", out var grade) == true) profile.Grade = grade.GetString() ?? "";
        if (input?.TryGetProperty("teacher", out var teacher) == true) profile.Teacher = teacher.GetString() ?? "";
        if (input?.TryGetProperty("doctor_name", out var doc) == true) profile.DoctorName = doc.GetString() ?? "";
        if (input?.TryGetProperty("emergency_contact", out var ec) == true) profile.EmergencyContact = ec.GetString() ?? "";
        if (input?.TryGetProperty("medical_notes", out var mn) == true) profile.MedicalNotes = mn.GetString() ?? "";
        if (input?.TryGetProperty("notes", out var notes) == true) profile.Notes = notes.GetString() ?? "";
        if (input?.TryGetProperty("date_of_birth", out var dob) == true) profile.DateOfBirth = dob.GetString();

        if (input?.TryGetProperty("add_allergy", out var allergy) == true)
        {
            var val = allergy.GetString();
            if (!string.IsNullOrEmpty(val) && !profile.Allergies.Contains(val, StringComparer.OrdinalIgnoreCase))
                profile.Allergies.Add(val);
        }
        if (input?.TryGetProperty("add_like", out var like) == true)
        {
            var val = like.GetString();
            if (!string.IsNullOrEmpty(val) && !profile.Likes.Contains(val, StringComparer.OrdinalIgnoreCase))
                profile.Likes.Add(val);
        }
        if (input?.TryGetProperty("add_dislike", out var dislike) == true)
        {
            var val = dislike.GetString();
            if (!string.IsNullOrEmpty(val) && !profile.Dislikes.Contains(val, StringComparer.OrdinalIgnoreCase))
                profile.Dislikes.Add(val);
        }
        if (input?.TryGetProperty("add_nickname", out var nick) == true)
        {
            var val = nick.GetString();
            if (!string.IsNullOrEmpty(val) && !profile.Nicknames.Contains(val, StringComparer.OrdinalIgnoreCase))
                profile.Nicknames.Add(val);
        }
        if (input?.TryGetProperty("add_activity", out var activityEl) == true)
        {
            var activity = new FamilyActivity
            {
                Name = activityEl.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "",
                DayOfWeek = activityEl.TryGetProperty("day_of_week", out var dw) ? dw.GetString() ?? "" : "",
                Time = activityEl.TryGetProperty("time", out var t) ? t.GetString() ?? "" : "",
                Location = activityEl.TryGetProperty("location", out var l) ? l.GetString() ?? "" : ""
            };
            if (!string.IsNullOrEmpty(activity.Name))
                profile.Activities.Add(activity);
        }
        if (input?.TryGetProperty("remove_activity", out var removeAct) == true)
        {
            var actName = removeAct.GetString();
            if (!string.IsNullOrEmpty(actName))
                profile.Activities.RemoveAll(a => a.Name.Equals(actName, StringComparison.OrdinalIgnoreCase));
        }

        profile.UpdatedAt = DateTime.UtcNow;
        await _familyProfileRepository.UpsertAsync(profile, cancellationToken);
        return $"Updated profile for {name}.";
    }

    private async Task<string> HandleViewFamilyProfile(JsonElement? input, CancellationToken cancellationToken)
    {
        var name = input?.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrEmpty(name)) return "Error: 'name' is required.";

        var profile = await _familyProfileRepository.GetByNameAsync(name, cancellationToken);
        if (profile == null) return $"No profile found for '{name}'.";

        return JsonSerializer.Serialize(profile);
    }

    private async Task<string> HandleListFamilyProfiles(CancellationToken cancellationToken)
    {
        var profiles = await _familyProfileRepository.GetAllActiveAsync(cancellationToken);
        if (profiles.Count == 0) return "No family profiles found.";

        return JsonSerializer.Serialize(profiles.Select(p => new
        {
            name = p.Name,
            age = p.Age,
            profileType = p.ProfileType,
            school = p.School,
            activitiesCount = p.Activities.Count
        }));
    }

    private async Task<string> HandleDeactivateFamilyProfile(JsonElement? input, CancellationToken cancellationToken)
    {
        var name = input?.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrEmpty(name)) return "Error: 'name' is required.";

        var removed = await _familyProfileRepository.DeactivateAsync(name, cancellationToken);
        return removed ? $"Removed profile for {name}." : $"No profile found for '{name}'.";
    }

    #endregion

    #region Kid Contact Tools

    private async Task<string> HandleRegisterKidContact(JsonElement? input, CancellationToken cancellationToken)
    {
        var profileName = input?.GetProperty("profile_name").GetString() ?? "";
        var smsNumber = input?.TryGetProperty("sms_number", out var sms) == true ? sms.GetString() : null;
        var whatsAppNumber = input?.TryGetProperty("whatsapp_number", out var wa) == true ? wa.GetString() : null;

        if (string.IsNullOrEmpty(profileName)) return "Error: 'profile_name' is required.";
        if (string.IsNullOrEmpty(smsNumber) && string.IsNullOrEmpty(whatsAppNumber))
            return "Error: at least one of 'sms_number' or 'whatsapp_number' is required.";

        var profile = await _familyProfileRepository.GetByNameAsync(profileName, cancellationToken);
        if (profile == null) return $"No family profile found for '{profileName}'. Create one first.";

        var existing = await _kidSmsRepository.GetByProfileNameAsync(profileName, cancellationToken);
        var kid = existing ?? new KidSmsUser
        {
            Id = $"kidsms-{smsNumber ?? whatsAppNumber}",
            LinkedProfileName = profileName,
            DisplayName = profile.Name,
            AddedBy = _currentMember?.PhoneNumber ?? ""
        };

        if (!string.IsNullOrEmpty(smsNumber)) kid.SmsPhoneNumber = smsNumber;
        if (!string.IsNullOrEmpty(whatsAppNumber))
        {
            kid.WhatsAppPhoneNumber = whatsAppNumber;
            kid.PreferredChannel = "WhatsApp";
        }

        kid.UpdatedAt = DateTime.UtcNow;
        await _kidSmsRepository.UpsertAsync(kid, cancellationToken);
        _logger.LogInformation("Registered kid contact for {ProfileName}", profileName);

        var channels = new List<string>();
        if (!string.IsNullOrEmpty(kid.SmsPhoneNumber)) channels.Add($"SMS: {kid.SmsPhoneNumber}");
        if (!string.IsNullOrEmpty(kid.WhatsAppPhoneNumber)) channels.Add($"WhatsApp: {kid.WhatsAppPhoneNumber}");
        return $"Registered contact for {profile.Name}. Channels: {string.Join(", ", channels)}. Preferred: {kid.PreferredChannel}.";
    }

    private async Task<string> HandleUpdateKidChannel(JsonElement? input, CancellationToken cancellationToken)
    {
        var profileName = input?.GetProperty("profile_name").GetString() ?? "";
        if (string.IsNullOrEmpty(profileName)) return "Error: 'profile_name' is required.";

        var kid = await _kidSmsRepository.GetByProfileNameAsync(profileName, cancellationToken);
        if (kid == null) return $"No contact record found for '{profileName}'. Register one first.";

        if (input?.TryGetProperty("preferred_channel", out var pc) == true)
            kid.PreferredChannel = pc.GetString() ?? kid.PreferredChannel;
        if (input?.TryGetProperty("sms_number", out var sms) == true)
            kid.SmsPhoneNumber = sms.GetString();
        if (input?.TryGetProperty("whatsapp_number", out var wa) == true)
            kid.WhatsAppPhoneNumber = wa.GetString();

        kid.UpdatedAt = DateTime.UtcNow;
        await _kidSmsRepository.UpsertAsync(kid, cancellationToken);
        return $"Updated contact settings for {kid.DisplayName}. Preferred channel: {kid.PreferredChannel}.";
    }

    private async Task<string> HandleSendKidAlert(JsonElement? input, CancellationToken cancellationToken)
    {
        var profileName = input?.GetProperty("profile_name").GetString() ?? "";
        var message = input?.GetProperty("message").GetString() ?? "";

        if (string.IsNullOrEmpty(profileName)) return "Error: 'profile_name' is required.";
        if (string.IsNullOrEmpty(message)) return "Error: 'message' is required.";

        var kid = await _kidSmsRepository.GetByProfileNameAsync(profileName, cancellationToken);
        if (kid == null) return $"No contact record found for '{profileName}'. Register their number first.";

        string? deliveryNumber = null;
        var channelUsed = kid.PreferredChannel;

        if (kid.PreferredChannel == "WhatsApp" && !string.IsNullOrEmpty(kid.WhatsAppPhoneNumber))
            deliveryNumber = kid.WhatsAppPhoneNumber;
        else if (!string.IsNullOrEmpty(kid.SmsPhoneNumber))
        {
            deliveryNumber = kid.SmsPhoneNumber;
            channelUsed = "SMS";
        }
        else if (!string.IsNullOrEmpty(kid.WhatsAppPhoneNumber))
        {
            deliveryNumber = kid.WhatsAppPhoneNumber;
            channelUsed = "WhatsApp";
        }

        if (deliveryNumber == null)
            return $"No delivery channel available for {kid.DisplayName}. Register a phone number first.";

        var channel = channelUsed == "WhatsApp" ? MessageChannel.WhatsApp : MessageChannel.SMS;
        await _whatsAppService.SendOnChannelAsync(deliveryNumber, channel, message, cancellationToken);

        _logger.LogInformation("Sent kid alert to {KidName} via {Channel}", kid.DisplayName, channelUsed);
        var channelDesc = channelUsed == "SMS" ? $"Texted {kid.DisplayName}'s watch" : $"Sent {kid.DisplayName} a message on WhatsApp";
        return $"{channelDesc}: \"{message}\"";
    }

    #endregion

    private static List<object> BuildToolDefinitions()
    {
        return new List<object>
        {
            new
            {
                name = "get_calendar_events",
                description = "Get calendar events for a date range from the family calendar. Use a wide range (e.g. 6 months) when searching for a specific event by name. Returns event IDs needed for cancellation.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["from"] = new { type = "string", description = "Start date/time in ISO 8601 format (e.g., 2026-04-07T00:00:00-05:00)" },
                        ["to"] = new { type = "string", description = "End date/time in ISO 8601 format (e.g., 2026-04-07T23:59:59-05:00)" }
                    },
                    required = new[] { "from", "to" }
                }
            },
            new
            {
                name = "create_calendar_event",
                description = "Create a new event on the family calendar. Can optionally invite attendees by email address. Use get_family_members first to look up email addresses when the user wants to invite family members.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["subject"] = new { type = "string", description = "The event title/subject" },
                        ["start"] = new { type = "string", description = "Start date/time in ISO 8601 format" },
                        ["end"] = new { type = "string", description = "End date/time in ISO 8601 format" },
                        ["location"] = new { type = "string", description = "Event location (optional)" },
                        ["attendees"] = new { type = "array", items = new { type = "string" }, description = "List of attendee email addresses to invite (optional)" }
                    },
                    required = new[] { "subject", "start", "end" }
                }
            },
            new
            {
                name = "cancel_calendar_event",
                description = "Cancel/delete a calendar event. Use get_calendar_events first to find the event ID.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["event_id"] = new { type = "string", description = "The ID of the event to cancel (from get_calendar_events results)" }
                    },
                    required = new[] { "event_id" }
                }
            },
            new
            {
                name = "get_family_members",
                description = "Get a list of all family members with their names and email addresses. Use this to look up email addresses when inviting family members to calendar events.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "save_memory",
                description = "Save a fact or piece of information to the shared family knowledge base. Use this when a family member says 'remember', 'note', 'save', or 'don't forget' something. If a memory with the same key exists, it will be updated.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["key"] = new { type = "string", description = "A short normalized lowercase key for the fact (e.g., 'george shoe size', 'wifi password', 'emma teacher')" },
                        ["value"] = new { type = "string", description = "The full natural language fact to remember (e.g., 'George\\'s shoe size is 9')" }
                    },
                    required = new[] { "key", "value" }
                }
            },
            new
            {
                name = "delete_memory",
                description = "Delete/forget a fact from the shared family knowledge base. Use this when a family member says 'forget', 'delete', or 'remove' a memory.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["key"] = new { type = "string", description = "The normalized lowercase key of the memory to delete (e.g., 'george shoe size')" }
                    },
                    required = new[] { "key" }
                }
            },
            new
            {
                name = "list_memories",
                description = "List all facts stored in the shared family knowledge base. Use when a family member asks 'what do you remember?' or similar.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "send_email",
                description = "Send an email from the shared BDA family mailbox.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["to"] = new { type = "string", description = "Recipient email address" },
                        ["subject"] = new { type = "string", description = "Email subject line" },
                        ["body"] = new { type = "string", description = "Email body text" }
                    },
                    required = new[] { "to", "subject", "body" }
                }
            },
            new
            {
                name = "add_monitored_mailbox",
                description = "Add a family member's email mailbox to be monitored for actionable emails from whitelisted senders.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["email_address"] = new { type = "string", description = "The email address of the mailbox to monitor" },
                        ["display_name"] = new { type = "string", description = "A friendly name for the mailbox (e.g., 'Travis Work Email')" }
                    },
                    required = new[] { "email_address", "display_name" }
                }
            },
            new
            {
                name = "remove_monitored_mailbox",
                description = "Stop monitoring a family member's email mailbox.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["email_address"] = new { type = "string", description = "The email address of the mailbox to stop monitoring" }
                    },
                    required = new[] { "email_address" }
                }
            },
            new
            {
                name = "list_monitored_mailboxes",
                description = "List all currently monitored email mailboxes.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "add_whitelisted_sender",
                description = "Add a sender to the whitelist. Accepts a full email (school@usd123.edu) or a domain (@usd123.edu) to match all senders from that domain. Only emails from whitelisted senders will be analyzed in monitored mailboxes.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["email_address"] = new { type = "string", description = "The sender email or domain to whitelist. Use full email (school@usd123.edu) for one sender, or @domain (@usd123.edu) for all senders on that domain." },
                        ["display_name"] = new { type = "string", description = "A friendly name for the sender (e.g., 'School District')" }
                    },
                    required = new[] { "email_address" }
                }
            },
            new
            {
                name = "remove_whitelisted_sender",
                description = "Remove a sender from the email whitelist.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["email_address"] = new { type = "string", description = "The sender email address to remove from the whitelist" }
                    },
                    required = new[] { "email_address" }
                }
            },
            new
            {
                name = "list_whitelisted_senders",
                description = "List all whitelisted email senders.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "send_joke",
                description = "Send a random joke to another family member via WhatsApp. Use when the user says 'send [name] a joke', 'tell [name] a joke', or similar. Resolve nicknames like 'dad', 'mom' to the actual family member name.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["target_name"] = new { type = "string", description = "Name or nickname of the family member to send the joke to (e.g., 'Travis', 'dad', 'mom')" }
                    },
                    required = new[] { "target_name" }
                }
            },
            new
            {
                name = "send_fun_fact",
                description = "Send a random fun fact to another family member via WhatsApp. Use when the user says 'send [name] a fact', 'tell [name] something interesting', or similar. Can optionally specify a topic like Kansas, space, animals, history, etc.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["target_name"] = new { type = "string", description = "Name or nickname of the family member to send the fact to (e.g., 'Travis', 'dad', 'mom')" },
                        ["topic"] = new { type = "string", description = "Optional topic for the fact (e.g., 'Kansas', 'space', 'dinosaurs', 'animals'). If omitted, a random topic is chosen." }
                    },
                    required = new[] { "target_name" }
                }
            },
            new
            {
                name = "add_affirmation",
                description = "Add an affirmation to the shared family pool.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["text"] = new { type = "string", description = "The affirmation text to add to the pool" }
                    },
                    required = new[] { "text" }
                }
            },
            new
            {
                name = "remove_affirmation",
                description = "Remove an affirmation from the pool.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["text"] = new { type = "string", description = "The exact text of the affirmation to remove" }
                    },
                    required = new[] { "text" }
                }
            },
            new
            {
                name = "list_affirmations",
                description = "List all affirmations in the shared pool.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "add_feature_request",
                description = "Add a feature request or idea for BDA improvements. Use when a family member says 'add feature request', 'request', 'feature idea', or 'suggest'.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["description"] = new { type = "string", description = "The feature request description" }
                    },
                    required = new[] { "description" }
                }
            },
            new
            {
                name = "remove_feature_request",
                description = "Remove a feature request. Use when a family member says 'remove feature request' or 'delete feature request'.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["description"] = new { type = "string", description = "The feature request description or keywords to match" }
                    },
                    required = new[] { "description" }
                }
            },
            new
            {
                name = "list_feature_requests",
                description = "List all current feature requests. Use when a family member asks to see feature requests or ideas.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "clear_conversation",
                description = "Clear the current conversation history. Use this when the user asks to clear their chat, start fresh, reset the conversation, or forget what was discussed. This only clears the chat history — family memories are not affected.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "set_preference",
                description = "Save a personal preference for the current family member. Use for structured preferences (briefing length, communication style, quiet hours, reminder lead time) or free-form learned preferences (diet, interests, units, etc.).",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["field"] = new { type = "string", description = "Structured field name: briefingLength, communicationStyle, quietHoursStart, quietHoursEnd, defaultReminderLeadTimeHours. Null for free-form." },
                        ["value"] = new { type = "string", description = "Value for the structured field." },
                        ["learned_key"] = new { type = "string", description = "Short key for free-form preference (e.g. 'diet', 'units', 'humor style')." },
                        ["learned_value"] = new { type = "string", description = "Value for the free-form preference (e.g. 'vegetarian', 'metric', 'dad jokes')." }
                    },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "list_preferences",
                description = "List all saved preferences for the current family member. Use when they ask to see their preferences or what you know about them.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "remove_preference",
                description = "Remove a saved preference for the current family member. Use when they ask to forget a preference.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["key"] = new { type = "string", description = "The preference key to remove — either a structured field name or a learned preference key." }
                    },
                    required = new[] { "key" }
                }
            },
            new
            {
                name = "create_family_profile",
                description = "Create a profile for a family member who doesn't use BDA directly (kids, pets). Use when an adult says 'add family profile for...' or 'add Teddy, age 8'.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new { type = "string", description = "Name of the family member" },
                        ["age"] = new { type = "integer", description = "Age (optional)" },
                        ["profile_type"] = new { type = "string", description = "Type: child, pet, or other (default: child)" },
                        ["date_of_birth"] = new { type = "string", description = "Date of birth in ISO format (optional)" },
                        ["school"] = new { type = "string", description = "School name (optional)" },
                        ["grade"] = new { type = "string", description = "Grade level (optional)" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "update_family_profile",
                description = "Update a family profile. Use for any profile detail: teacher, allergies, likes, dislikes, activities, school, grade, medical notes, etc. Call once per distinct update field.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new { type = "string", description = "Name of the family member to update" },
                        ["age"] = new { type = "integer" },
                        ["school"] = new { type = "string" },
                        ["grade"] = new { type = "string" },
                        ["teacher"] = new { type = "string" },
                        ["doctor_name"] = new { type = "string" },
                        ["emergency_contact"] = new { type = "string" },
                        ["medical_notes"] = new { type = "string" },
                        ["notes"] = new { type = "string" },
                        ["date_of_birth"] = new { type = "string" },
                        ["add_allergy"] = new { type = "string", description = "Add an allergy" },
                        ["add_like"] = new { type = "string", description = "Add a like/interest" },
                        ["add_dislike"] = new { type = "string", description = "Add a dislike" },
                        ["add_nickname"] = new { type = "string", description = "Add a nickname" },
                        ["add_activity"] = new { type = "object", properties = new Dictionary<string, object>
                        {
                            ["name"] = new { type = "string" },
                            ["day_of_week"] = new { type = "string" },
                            ["time"] = new { type = "string" },
                            ["location"] = new { type = "string" }
                        }},
                        ["remove_activity"] = new { type = "string", description = "Remove an activity by name" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "view_family_profile",
                description = "View the full profile for a family member.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new { type = "string", description = "Name of the family member" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "list_family_profiles",
                description = "List all family profiles.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "deactivate_family_profile",
                description = "Remove/deactivate a family profile.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new { type = "string", description = "Name of the family member" }
                    },
                    required = new[] { "name" }
                }
            },
            new
            {
                name = "register_kid_contact",
                description = "Register a kid's contact number (SMS watch or WhatsApp) and link it to their family profile. This allows the kid to interact with BDA.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["profile_name"] = new { type = "string", description = "Name matching a family profile" },
                        ["sms_number"] = new { type = "string", description = "SMS phone number in E.164 format (watch number)" },
                        ["whatsapp_number"] = new { type = "string", description = "WhatsApp number in E.164 format" }
                    },
                    required = new[] { "profile_name" }
                }
            },
            new
            {
                name = "update_kid_channel",
                description = "Update a kid's contact settings — preferred channel, SMS number, or WhatsApp number.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["profile_name"] = new { type = "string", description = "Name matching a family profile" },
                        ["preferred_channel"] = new { type = "string", description = "SMS or WhatsApp" },
                        ["sms_number"] = new { type = "string", description = "New SMS number" },
                        ["whatsapp_number"] = new { type = "string", description = "New WhatsApp number" }
                    },
                    required = new[] { "profile_name" }
                }
            },
            new
            {
                name = "send_kid_alert",
                description = "Send a message to a kid on their preferred channel. Use when an adult says 'text Teddy that...' or 'send Emma a reminder...'.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["profile_name"] = new { type = "string", description = "Name matching a family profile" },
                        ["message"] = new { type = "string", description = "The message to send (keep it short and kid-friendly)" }
                    },
                    required = new[] { "profile_name", "message" }
                }
            },
            new
            {
                type = "web_search_20250305",
                name = "web_search",
                max_uses = 3
            }
        };
    }

    private static List<object> BuildKidToolDefinitions()
    {
        return new List<object>
        {
            new
            {
                name = "get_calendar_events",
                description = "Get calendar events for a date range.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["from"] = new { type = "string", description = "Start date/time in ISO 8601 format" },
                        ["to"] = new { type = "string", description = "End date/time in ISO 8601 format" }
                    },
                    required = new[] { "from", "to" }
                }
            },
            new
            {
                name = "send_joke",
                description = "Send a joke to a family member.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["recipient_name"] = new { type = "string", description = "Name or nickname of the family member" }
                    },
                    required = new[] { "recipient_name" }
                }
            },
            new
            {
                name = "send_fun_fact",
                description = "Send a fun fact to a family member.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["recipient_name"] = new { type = "string", description = "Name or nickname" },
                        ["topic"] = new { type = "string", description = "Topic for the fact (optional)" }
                    },
                    required = new[] { "recipient_name" }
                }
            },
            new
            {
                name = "add_feature_request",
                description = "Submit an idea for BDA improvements.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["description"] = new { type = "string", description = "The feature idea" }
                    },
                    required = new[] { "description" }
                }
            },
            new
            {
                name = "list_memories",
                description = "List saved family memories.",
                input_schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                }
            },
            new
            {
                type = "web_search_20250305",
                name = "web_search",
                max_uses = 3
            }
        };
    }

    public string BuildSystemPrompt(
        FamilyMember member,
        DateTimeOffset localNow,
        IReadOnlyList<FamilyMemory>? memories = null,
        MemberPreferences? preferences = null,
        IReadOnlyList<SessionSummary>? sessionSummaries = null,
        IReadOnlyList<FamilyProfile>? profiles = null,
        IReadOnlyList<KidSmsUser>? kidContacts = null)
    {
        var prompt = $"""
            You are {_assistantOptions.Name}, a helpful family AI assistant.
            You are currently speaking with {member.Name}.
            Today is {localNow:dddd, MMMM d, yyyy}. Local time is {localNow:h:mm tt} ({member.Timezone}).
            You have access to the family calendar and the shared BDA email inbox.
            Use the provided tools to check the calendar, create events, and send emails — do not guess or make up data.
            When asked about calendar events, always use the get_calendar_events tool to check.
            When searching for a specific event by name, search a wide range (at least 6 months ahead) to ensure you find it.
            If you don't find an event in your initial search, try a broader date range before telling the user it doesn't exist.
            Calendar events may be prefixed with a family member's name (e.g., "Teddy - Miguel's Birthday Party" or "Oscar - Dentist").
            When searching for an event, look for partial matches anywhere in the event subject, not just exact title matches.
            When the user wants to invite family members to an event, use get_family_members to look up their email addresses first.
            When the user says "invite all family members" or "invite everyone", look up all family members and add all their emails as attendees.
            To cancel an event, first use get_calendar_events to find it and get its ID, then use cancel_calendar_event.
            When asked to send an email, use the send_email tool.
            When a family member asks you to remember, note, or save something, use the save_memory tool.
            When asked to forget or delete a memory, use the delete_memory tool.
            When asked what you remember, use the list_memories tool.
            You can also use the family knowledge base below to answer questions — treat these as ground truth.
            You can manage email monitoring — add/remove monitored mailboxes and whitelisted senders.
            When a user wants to monitor a mailbox or watch emails from a sender, use the appropriate tool.
            You can send jokes or fun facts to family members. When asked to "send [name] a joke" or "tell [name] a fun fact", use the send_joke or send_fun_fact tool. Facts can be about any topic — if the user specifies one (e.g., "Kansas fact", "space fact"), pass it as the topic. Family members may be referred to by nicknames like "dad", "mom", etc.
            Family members can submit feature requests for BDA improvements. Use the feature request tools when asked to add, remove, or list feature requests or ideas.
            You have access to web search. Use it when a question requires current information such as news, prices, business hours, scores, weather, movie times, or recent events. Also use it when asked to "search", "look up", "google", or "find" something.
            Do not use web search for questions answerable from family memory, conversation history, general knowledge, math, or stable facts.
            When answering with search results, summarize in plain conversational language for WhatsApp. Maximum 5 sentences. No URLs, no source citations, no markdown formatting. Do not mention that you searched unless asked.
            Family members can add affirmations to a shared pool used in morning briefings. Use the affirmation tools when asked to add, remove, or list affirmations.
            When a family member asks to clear their chat, start fresh, reset the conversation, or forget what was discussed, use the clear_conversation tool. Reassure them that family memories are not affected.
            Family members can set, view, and remove personal preferences using the preference tools. When someone says "set my briefing to short", "I prefer detailed responses", "don't message me before 8am", "I'm vegetarian", etc., use set_preference. When they ask to see their preferences, use list_preferences. When they ask to forget a preference, use remove_preference.
            You can manage family profiles for kids and other non-user family members. Use create_family_profile to add a new profile, update_family_profile to modify details (teacher, allergies, likes, activities, etc.), view_family_profile to see full details, and list_family_profiles to see all profiles. When an adult mentions something about a kid's schedule, teacher, allergies, or interests, update the appropriate profile. For multi-field updates like "Teddy likes Minecraft and hates broccoli", call update_family_profile multiple times (once per field).
            You can register kids' contact numbers (SMS watches or WhatsApp) using register_kid_contact, update their preferred communication channel with update_kid_channel, and send them direct messages with send_kid_alert. When an adult says "text Teddy that..." or "send Emma a reminder...", use send_kid_alert.
            Be warm, concise, and practical. You are not a chatbot — you are a capable assistant.
            """;

        // Member preferences
        if (preferences != null)
        {
            prompt += $"\n\n## {member.Name}'s Preferences\n";
            prompt += $"Communication style: {preferences.CommunicationStyle}\n";
            prompt += $"Briefing length: {preferences.BriefingLength}\n";

            if (!string.IsNullOrEmpty(preferences.QuietHoursStart) || !string.IsNullOrEmpty(preferences.QuietHoursEnd))
            {
                prompt += $"Quiet hours: {preferences.QuietHoursStart ?? "22:00"} to {preferences.QuietHoursEnd ?? "07:00"}\n";
            }

            prompt += $"Default reminder lead time: {preferences.DefaultReminderLeadTimeHours} hours\n";

            if (preferences.TopicsOfInterest.Count > 0)
                prompt += $"Topics of interest: {string.Join(", ", preferences.TopicsOfInterest)}\n";

            if (preferences.TopicsToAvoid.Count > 0)
                prompt += $"Topics to avoid: {string.Join(", ", preferences.TopicsToAvoid)}\n";

            if (preferences.LearnedPreferences.Count > 0)
            {
                prompt += "\nAdditional preferences:\n";
                foreach (var kvp in preferences.LearnedPreferences)
                {
                    prompt += $"- {kvp.Key}: {kvp.Value}\n";
                }
            }
        }

        // Family knowledge base
        if (memories != null && memories.Count > 0)
        {
            prompt += "\n\n## Family Knowledge Base\nThe following facts have been saved by your family. Treat these as ground truth when answering questions:\n\n";
            foreach (var memory in memories)
            {
                prompt += $"- {memory.Value} (saved by {memory.CreatedByName})\n";
            }
        }

        // Family profiles
        if (profiles != null && profiles.Count > 0)
        {
            prompt += "\n\n## Family Profiles\nThe following are profiles for family members who do not use BDA directly:\n\n";
            foreach (var p in profiles)
            {
                prompt += $"**{p.Name}";
                if (p.Age.HasValue) prompt += $" (age {p.Age})";
                prompt += "**\n";
                if (!string.IsNullOrEmpty(p.School))
                {
                    prompt += $"School: {p.School}";
                    if (!string.IsNullOrEmpty(p.Grade)) prompt += $", {p.Grade}";
                    if (!string.IsNullOrEmpty(p.Teacher)) prompt += $", Teacher: {p.Teacher}";
                    prompt += "\n";
                }
                if (p.Allergies.Count > 0) prompt += $"Allergies: {string.Join(", ", p.Allergies)}\n";
                if (p.Likes.Count > 0 || p.Dislikes.Count > 0)
                {
                    var parts = new List<string>();
                    if (p.Likes.Count > 0) parts.Add($"Likes: {string.Join(", ", p.Likes)}");
                    if (p.Dislikes.Count > 0) parts.Add($"Dislikes: {string.Join(", ", p.Dislikes)}");
                    prompt += string.Join(" | ", parts) + "\n";
                }
                if (p.Activities.Count > 0)
                {
                    var acts = p.Activities.Select(a =>
                    {
                        var desc = $"{a.Name} ({a.DayOfWeek}s {a.Time}";
                        if (!string.IsNullOrEmpty(a.Location)) desc += $", {a.Location}";
                        desc += ")";
                        return desc;
                    });
                    prompt += $"Activities: {string.Join(", ", acts)}\n";
                }
                if (!string.IsNullOrEmpty(p.Notes)) prompt += $"Notes: {p.Notes}\n";

                // Show kid contact info if registered
                var kidContact = kidContacts?.FirstOrDefault(k =>
                    k.LinkedProfileName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                if (kidContact != null)
                {
                    var channels = new List<string>();
                    if (!string.IsNullOrEmpty(kidContact.SmsPhoneNumber))
                        channels.Add($"SMS watch: {kidContact.SmsPhoneNumber}");
                    if (!string.IsNullOrEmpty(kidContact.WhatsAppPhoneNumber))
                        channels.Add($"WhatsApp: {kidContact.WhatsAppPhoneNumber}");
                    prompt += $"Contact: {string.Join(", ", channels)} (preferred: {kidContact.PreferredChannel})\n";
                }

                prompt += "\n";
            }
        }

        // Session summaries
        if (sessionSummaries != null && sessionSummaries.Count > 0)
        {
            prompt += $"\n\n## Conversation History Summary\nThe following are summaries of previous conversations with {member.Name}:\n\n";
            foreach (var summary in sessionSummaries)
            {
                prompt += $"**{summary.SessionDate:MMMM d, yyyy}** ({summary.MessageCount} messages):\n{summary.Summary}\n\n";
            }
        }

        return prompt;
    }

    public string BuildKidSystemPrompt(KidSmsUser kid, FamilyProfile profile, DateTimeOffset localNow)
    {
        var prompt = $"""
            You are {_assistantOptions.Name}, a friendly and helpful family assistant.
            You are currently talking with {kid.DisplayName}, who is {profile.Age?.ToString() ?? "a kid"} years old.
            Today is {localNow:dddd, MMMM d, yyyy}. Local time is {localNow:h:mm tt} CT.

            You are talking over SMS on a kids smartwatch — keep ALL responses SHORT (3 sentences maximum) because the screen is tiny.

            IMPORTANT CONTENT RULES — follow these strictly:
            - Language must be age-appropriate for a child under 13 at all times
            - Never discuss violence, adult themes, relationships, or anything inappropriate for children
            - Keep responses positive, encouraging, and fun
            - If asked anything inappropriate, respond: "That's not something I can help with — ask a mom or dad!"
            - Do not discuss other family members' personal details
            - Do not reveal family memory contents beyond what directly helps {kid.DisplayName}

            FORMATTING: SMS on a kids watch has a tiny screen. Maximum 3 sentences. No markdown. Plain text only. Simple words.
            """;

        // Profile summary
        prompt += $"\n\nWhat you know about {kid.DisplayName}:\n";
        if (!string.IsNullOrEmpty(profile.School))
        {
            prompt += $"School: {profile.School}";
            if (!string.IsNullOrEmpty(profile.Grade)) prompt += $", {profile.Grade}";
            if (!string.IsNullOrEmpty(profile.Teacher)) prompt += $", Teacher: {profile.Teacher}";
            prompt += "\n";
        }
        if (profile.Likes.Count > 0) prompt += $"Likes: {string.Join(", ", profile.Likes)}\n";
        if (profile.Activities.Count > 0)
        {
            var acts = profile.Activities.Select(a => $"{a.Name} ({a.DayOfWeek}s {a.Time})");
            prompt += $"Activities: {string.Join(", ", acts)}\n";
        }

        prompt += $"""

            You can help {kid.DisplayName} with:
            - Their schedule and activities ("What do I have today?")
            - Homework help (age-appropriate explanations only)
            - Fun facts and jokes
            - Reminders and timers
            - Simple questions and conversations
            """;

        return prompt;
    }

    public static List<ClaudeMessage> BuildMessages(ConversationHistory history, string userMessage)
    {
        var messages = history.CurrentSessionMessages
            .Select(m => new ClaudeMessage { Role = m.Role, Content = new List<object> { new ContentBlock { Type = "text", Text = m.Content } } })
            .ToList();

        messages.Add(new ClaudeMessage { Role = "user", Content = new List<object> { new ContentBlock { Type = "text", Text = userMessage } } });

        return messages;
    }
}

internal class ClaudeRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("system")]
    public string System { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ClaudeMessage> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<object>? Tools { get; set; }
}

public class ClaudeMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object Content { get; set; } = new List<object>();
}

internal class ContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }
}

internal class ClaudeResponse
{
    [JsonPropertyName("content")]
    public List<ContentBlock>? Content { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}
