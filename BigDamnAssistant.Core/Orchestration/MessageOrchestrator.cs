using System.Text.Json;
using System.Text.RegularExpressions;
using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BigDamnAssistant.Core.Orchestration;

public class MessageOrchestrator
{
    private static readonly HashSet<string> ConfirmationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "yeah", "yep", "sure", "ok", "okay", "yup", "add it", "go ahead", "do it", "please", "y"
    };

    private static readonly HashSet<string> RejectionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "nah", "cancel", "don't", "stop", "nope", "never mind", "skip", "n"
    };

    private readonly IClaudeService _claudeService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IConversationRepository _conversationRepository;
    private readonly IFamilyMemberRepository _familyMemberRepository;
    private readonly IInviteProcessingService _inviteProcessingService;
    private readonly ICalendarService _calendarService;
    private readonly IReminderService _reminderService;
    private readonly IEmailMonitoringRepository _emailMonitoringRepository;
    private readonly IMemberPreferencesRepository _memberPreferencesRepository;
    private readonly ISessionCompressionService _sessionCompressionService;
    private readonly IPreferenceDetectionService _preferenceDetectionService;
    private readonly ILogger<MessageOrchestrator> _logger;
    private readonly AssistantOptions _assistantOptions;

    public MessageOrchestrator(
        IClaudeService claudeService,
        IWhatsAppService whatsAppService,
        IConversationRepository conversationRepository,
        IFamilyMemberRepository familyMemberRepository,
        IInviteProcessingService inviteProcessingService,
        ICalendarService calendarService,
        IReminderService reminderService,
        IEmailMonitoringRepository emailMonitoringRepository,
        IMemberPreferencesRepository memberPreferencesRepository,
        ISessionCompressionService sessionCompressionService,
        IPreferenceDetectionService preferenceDetectionService,
        ILogger<MessageOrchestrator> logger,
        IOptions<AssistantOptions> assistantOptions)
    {
        _claudeService = claudeService;
        _whatsAppService = whatsAppService;
        _conversationRepository = conversationRepository;
        _familyMemberRepository = familyMemberRepository;
        _inviteProcessingService = inviteProcessingService;
        _calendarService = calendarService;
        _reminderService = reminderService;
        _emailMonitoringRepository = emailMonitoringRepository;
        _memberPreferencesRepository = memberPreferencesRepository;
        _sessionCompressionService = sessionCompressionService;
        _preferenceDetectionService = preferenceDetectionService;
        _logger = logger;
        _assistantOptions = assistantOptions.Value;
    }

    public async Task HandleInboundWhatsAppAsync(
        string fromPhoneNumber,
        string messageBody,
        bool isGroupChat,
        string? mediaUrl = null,
        string? mediaContentType = null,
        CancellationToken cancellationToken = default)
    {
        var member = await _familyMemberRepository.GetByPhoneNumberAsync(fromPhoneNumber, cancellationToken);
        if (member is null)
        {
            _logger.LogWarning("Received message from unknown number");
            return;
        }

        if (isGroupChat && !messageBody.Contains(_assistantOptions.TriggerKeyword, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Group message ignored — no trigger keyword");
            return;
        }

        var conversation = await _conversationRepository.GetOrCreateAsync(fromPhoneNumber, cancellationToken);

        // Detect session boundary and compress previous session
        await TryCompressSessionAsync(conversation, member.Name, cancellationToken);

        // Check for pending email action (number selection or yes/no)
        var trimmedMessage = messageBody.Trim();
        if (IsEmailActionResponse(trimmedMessage))
        {
            var handled = await TryHandleEmailActionResponse(member, conversation, trimmedMessage, cancellationToken);
            if (handled)
                return;
        }

        // Check for pending action (e.g., birthday invite confirmation)
        if (conversation.PendingAction != null)
        {
            if (conversation.PendingAction.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogInformation("Pending action expired, clearing");
                conversation.PendingAction = null;
                await _conversationRepository.UpsertAsync(conversation, cancellationToken);
                // Fall through to normal processing
            }
            else if (conversation.PendingAction.ActionType == "BirthdayInviteConfirmation")
            {
                await HandleBirthdayInviteConfirmation(member, conversation, messageBody, cancellationToken);
                return;
            }
        }

        // Check for media attachment (potential birthday invite)
        if (!string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(mediaContentType))
        {
            await HandleMediaMessage(member, conversation, messageBody, mediaUrl, mediaContentType, cancellationToken);
            return;
        }

        // Normal message flow
        if (string.IsNullOrEmpty(messageBody))
        {
            _logger.LogWarning("Empty message body with no media");
            return;
        }

        _logger.LogInformation("Processing message from {MemberName} (group={IsGroup})", member.Name, isGroupChat);

        string response;
        try
        {
            response = await _claudeService.GetResponseAsync(member, conversation, messageBody, cancellationToken);
            _logger.LogInformation("Claude responded with {Length} chars", response.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude API failed for {MemberName}", member.Name);
            response = "I'm having trouble thinking right now. Give me a moment and try again.";
        }

        var maxMessages = _assistantOptions.MaxCurrentSessionMessages;
        conversation.AddMessage("user", messageBody, maxMessages);
        conversation.AddMessage("assistant", response, maxMessages);

        try
        {
            await _conversationRepository.UpsertAsync(conversation, cancellationToken);
            _logger.LogInformation("Conversation saved for {Phone}", fromPhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save conversation for {Phone}: {Message}", fromPhoneNumber, ex.Message);
        }

        try
        {
            await _whatsAppService.SendMessageAsync(fromPhoneNumber, response, cancellationToken);
            _logger.LogInformation("WhatsApp reply sent to {Phone}", fromPhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message to {Phone}: {Message}", fromPhoneNumber, ex.Message);
        }

        // Fire preference detection without blocking the response
        _ = DetectAndSavePreferenceAsync(member, messageBody, response, cancellationToken);
    }

    private async Task HandleMediaMessage(
        FamilyMember member,
        ConversationHistory conversation,
        string messageBody,
        string mediaUrl,
        string mediaContentType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing media message from {MemberName}: {MediaType}", member.Name, mediaContentType);

        // Check supported image types
        if (!mediaContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody.Length > 0 ? messageBody : "[sent a file]",
                "I can only read image files for birthday invites — try sending a photo of the invite.",
                cancellationToken);
            return;
        }

        // Download the image
        byte[] imageBytes;
        try
        {
            imageBytes = await _whatsAppService.DownloadMediaAsync(mediaUrl, cancellationToken);
            _logger.LogInformation("Downloaded media: {Size} bytes", imageBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Twilio media: {Message}", ex.Message);
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                "[sent an image]",
                "I had trouble reading that image — could you try sending it again?",
                cancellationToken);
            return;
        }

        // Extract invite details using Claude vision
        BirthdayInviteDetails? details;
        try
        {
            details = await _inviteProcessingService.ExtractInviteDetailsAsync(imageBytes, mediaContentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process invite image: {Message}", ex.Message);
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                "[sent an image]",
                "I couldn't read the invite details from that image — could you try a clearer photo?",
                cancellationToken);
            return;
        }

        if (details == null || !details.IsBirthdayInvite)
        {
            // Not a birthday invite — pass to normal Claude conversation with a note about the image
            _logger.LogInformation("Image is not a birthday invite, passing to normal flow");
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                "[sent an image]",
                "That doesn't look like a birthday invite — what would you like me to do with it?",
                cancellationToken);
            return;
        }

        // Check if we have enough data to create a calendar event
        if (string.IsNullOrEmpty(details.PartyDate) || string.IsNullOrEmpty(details.PartyTime))
        {
            var partialMessage = FormatInviteDetails(details);
            partialMessage += "\n\nI couldn't determine the date or time from the invite — can you tell me when the party is?";

            await SendAndSaveResponse(member.PhoneNumber, conversation,
                "[sent a birthday invite image]", partialMessage, cancellationToken);
            return;
        }

        // Store pending action and ask for confirmation
        var confirmationMessage = FormatInviteDetails(details);
        confirmationMessage += "\n\nShould I add this to the family calendar? I'll also set reminders to RSVP, get a gift, and a reminder the day before.";

        conversation.PendingAction = new PendingAction
        {
            ActionType = "BirthdayInviteConfirmation",
            Payload = JsonSerializer.Serialize(details),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        await SendAndSaveResponse(member.PhoneNumber, conversation,
            "[sent a birthday invite image]", confirmationMessage, cancellationToken);
    }

    private async Task HandleBirthdayInviteConfirmation(
        FamilyMember member,
        ConversationHistory conversation,
        string messageBody,
        CancellationToken cancellationToken)
    {
        var trimmed = messageBody.Trim();

        if (RejectionWords.Contains(trimmed))
        {
            conversation.PendingAction = null;
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody, "No problem, I've discarded the invite details.", cancellationToken);
            return;
        }

        if (!ConfirmationWords.Contains(trimmed))
        {
            // Not a clear yes/no — ask again
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody,
                "I wasn't sure if that's a yes or no — should I add the birthday party to the calendar?",
                cancellationToken);
            return;
        }

        // Confirmed — process the invite
        var details = JsonSerializer.Deserialize<BirthdayInviteDetails>(conversation.PendingAction!.Payload);
        conversation.PendingAction = null;

        if (details == null)
        {
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody, "Something went wrong reading the invite details. Could you send the photo again?",
                cancellationToken);
            return;
        }

        // Create calendar event
        try
        {
            var partyDateTime = DateTimeOffset.Parse($"{details.PartyDate}T{details.PartyTime}:00");
            var tz = TimeZoneInfo.FindSystemTimeZoneById(member.Timezone);
            var localPartyTime = new DateTimeOffset(
                DateTime.SpecifyKind(partyDateTime.DateTime, DateTimeKind.Unspecified),
                tz.GetUtcOffset(partyDateTime.DateTime));

            var location = !string.IsNullOrEmpty(details.VenueName) && !string.IsNullOrEmpty(details.VenueAddress)
                ? $"{details.VenueName}, {details.VenueAddress}"
                : details.VenueName.Length > 0 ? details.VenueName : details.VenueAddress;

            var calendarEvent = new CalendarEvent
            {
                Subject = $"🎂 {details.ChildName}'s Birthday Party",
                Start = localPartyTime,
                End = localPartyTime.AddHours(2),
                Location = string.IsNullOrEmpty(location) ? null : location
            };

            await _calendarService.CreateEventAsync(calendarEvent, cancellationToken);
            _logger.LogInformation("Created calendar event for {ChildName}'s birthday party", details.ChildName);

            // Create reminders
            var reminderMessages = await CreateBirthdayReminders(member, details, localPartyTime, cancellationToken);

            var response = $"Done! I've added {details.ChildName}'s birthday party to the family calendar and set the following reminders:\n\n";
            response += string.Join("\n", reminderMessages);
            response += "\n\nHave fun at the party! 🎉";

            await SendAndSaveResponse(member.PhoneNumber, conversation, messageBody, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create calendar event for birthday invite: {Message}", ex.Message);

            var fallback = $"I extracted the details but had trouble adding it to the calendar.\n\n";
            fallback += FormatInviteDetails(details);

            await SendAndSaveResponse(member.PhoneNumber, conversation, messageBody, fallback, cancellationToken);
        }
    }

    private async Task<List<string>> CreateBirthdayReminders(
        FamilyMember member,
        BirthdayInviteDetails details,
        DateTimeOffset partyTime,
        CancellationToken cancellationToken)
    {
        var reminders = new List<string>();
        var now = DateTimeOffset.UtcNow;

        // RSVP reminder: 2 days before RSVP deadline, or 7 days before party
        DateTimeOffset rsvpReminderTime;
        if (!string.IsNullOrEmpty(details.RsvpDeadline) && DateTimeOffset.TryParse(details.RsvpDeadline, out var rsvpDeadline))
        {
            rsvpReminderTime = rsvpDeadline.AddDays(-2);
        }
        else
        {
            rsvpReminderTime = partyTime.AddDays(-7);
        }

        if (rsvpReminderTime > now)
        {
            var rsvpMsg = $"Don't forget to RSVP for {details.ChildName}'s birthday party!";
            if (!string.IsNullOrEmpty(details.RsvpContact))
                rsvpMsg += $" Contact: {details.RsvpContact}";

            await _reminderService.CreateReminderAsync(member.PhoneNumber, rsvpMsg, rsvpReminderTime, cancellationToken);
            reminders.Add($"⏰ RSVP reminder: {rsvpReminderTime:MMMM d}");
        }

        // Gift reminder: 3 days before party
        var giftReminderTime = partyTime.AddDays(-3);
        if (giftReminderTime > now)
        {
            var giftMsg = $"Time to get a gift for {details.ChildName}'s birthday party on {partyTime:MMMM d}!";
            await _reminderService.CreateReminderAsync(member.PhoneNumber, giftMsg, giftReminderTime, cancellationToken);
            reminders.Add($"🎁 Get a gift reminder: {giftReminderTime:MMMM d}");
        }

        // Day-before reminder: day before at 9 AM member's local time
        var tz = TimeZoneInfo.FindSystemTimeZoneById(member.Timezone);
        var dayBefore = partyTime.AddDays(-1).Date;
        var dayBeforeLocal = new DateTimeOffset(dayBefore.Year, dayBefore.Month, dayBefore.Day, 9, 0, 0, tz.GetUtcOffset(dayBefore));
        if (dayBeforeLocal > now)
        {
            var dayBeforeMsg = $"{details.ChildName}'s birthday party is tomorrow at {partyTime:h:mm tt}";
            if (!string.IsNullOrEmpty(details.VenueName))
                dayBeforeMsg += $" at {details.VenueName}";
            dayBeforeMsg += ". Don't forget the gift!";

            await _reminderService.CreateReminderAsync(member.PhoneNumber, dayBeforeMsg, dayBeforeLocal, cancellationToken);
            reminders.Add($"📅 Day-before reminder: {dayBeforeLocal:MMMM d}");
        }

        return reminders;
    }

    private static string FormatInviteDetails(BirthdayInviteDetails details)
    {
        var msg = "I can see a birthday party invite! Here's what I found:\n\n";
        msg += $"🎂 Birthday party for {details.ChildName}\n";

        if (!string.IsNullOrEmpty(details.PartyDate) && !string.IsNullOrEmpty(details.PartyTime))
        {
            if (DateTimeOffset.TryParse($"{details.PartyDate}T{details.PartyTime}:00", out var dt))
                msg += $"📅 {dt:dddd, MMMM d} at {dt:h:mm tt}\n";
            else
                msg += $"📅 {details.PartyDate} at {details.PartyTime}\n";
        }
        else if (!string.IsNullOrEmpty(details.PartyDate))
        {
            msg += $"📅 {details.PartyDate}\n";
        }

        if (!string.IsNullOrEmpty(details.VenueName) || !string.IsNullOrEmpty(details.VenueAddress))
        {
            msg += "📍 ";
            if (!string.IsNullOrEmpty(details.VenueName))
                msg += details.VenueName;
            if (!string.IsNullOrEmpty(details.VenueName) && !string.IsNullOrEmpty(details.VenueAddress))
                msg += ", ";
            if (!string.IsNullOrEmpty(details.VenueAddress))
                msg += details.VenueAddress;
            msg += "\n";
        }

        if (!string.IsNullOrEmpty(details.RsvpDeadline) || !string.IsNullOrEmpty(details.RsvpContact))
        {
            msg += "📬 ";
            if (!string.IsNullOrEmpty(details.RsvpDeadline))
            {
                if (DateTimeOffset.TryParse(details.RsvpDeadline, out var rsvpDt))
                    msg += $"RSVP by {rsvpDt:MMMM d}";
                else
                    msg += $"RSVP by {details.RsvpDeadline}";
            }
            if (!string.IsNullOrEmpty(details.RsvpContact))
            {
                msg += !string.IsNullOrEmpty(details.RsvpDeadline) ? $" to {details.RsvpContact}" : $"RSVP to {details.RsvpContact}";
            }
            msg += "\n";
        }

        if (!string.IsNullOrEmpty(details.AdditionalDetails))
        {
            msg += $"\n{details.AdditionalDetails}\n";
        }

        if (details.MissingFields.Count > 0)
        {
            msg += $"\nNote: I couldn't make out the {string.Join(", ", details.MissingFields)} — you may want to add that manually.";
        }

        return msg;
    }

    private async Task SendAndSaveResponse(
        string phoneNumber,
        ConversationHistory conversation,
        string userMessage,
        string response,
        CancellationToken cancellationToken)
    {
        var maxMessages = _assistantOptions.MaxCurrentSessionMessages;
        conversation.AddMessage("user", userMessage, maxMessages);
        conversation.AddMessage("assistant", response, maxMessages);

        try
        {
            await _conversationRepository.UpsertAsync(conversation, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save conversation for {Phone}: {Message}", phoneNumber, ex.Message);
        }

        try
        {
            await _whatsAppService.SendMessageAsync(phoneNumber, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message to {Phone}: {Message}", phoneNumber, ex.Message);
        }
    }

    private static bool IsEmailActionResponse(string message)
    {
        return Regex.IsMatch(message, @"^[1-9]$")
            || ConfirmationWords.Contains(message)
            || RejectionWords.Contains(message);
    }

    private async Task<bool> TryHandleEmailActionResponse(
        FamilyMember member,
        ConversationHistory conversation,
        string messageBody,
        CancellationToken cancellationToken)
    {
        var unresolvedActions = await _emailMonitoringRepository.GetUnresolvedActionsAsync(cancellationToken);
        if (unresolvedActions.Count == 0)
            return false;

        // Use the most recent unresolved action
        var action = unresolvedActions.OrderByDescending(a => a.NotifiedAt).First();

        // Check if expired
        if (action.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogInformation("Email action {ActionId} has expired", action.Id);
            return false;
        }

        // Already resolved
        if (action.ResolvedBy != null)
        {
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody, $"Already handled by {action.ResolvedBy} ✅", cancellationToken);
            return true;
        }

        // Rejection
        if (RejectionWords.Contains(messageBody))
        {
            action.ResolvedBy = member.Name;
            action.ResolvedAt = DateTime.UtcNow;
            await _emailMonitoringRepository.UpsertActionAsync(action, cancellationToken);

            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody, "Got it, skipping that email action.", cancellationToken);
            return true;
        }

        // Determine which action to execute
        EmailSuggestedAction? selectedAction = null;

        if (action.SuggestedActions.Count == 1 && ConfirmationWords.Contains(messageBody))
        {
            selectedAction = action.SuggestedActions[0];
        }
        else if (int.TryParse(messageBody, out var actionNumber) &&
                 actionNumber >= 1 && actionNumber <= action.SuggestedActions.Count)
        {
            selectedAction = action.SuggestedActions[actionNumber - 1];
        }

        if (selectedAction == null)
            return false;

        // Execute the selected action
        try
        {
            var resultMessage = await ExecuteEmailAction(member, selectedAction, action.EmailSubject, cancellationToken);

            action.ResolvedBy = member.Name;
            action.ResolvedAt = DateTime.UtcNow;
            await _emailMonitoringRepository.UpsertActionAsync(action, cancellationToken);

            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody, resultMessage, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute email action for {ActionId}", action.Id);
            await SendAndSaveResponse(member.PhoneNumber, conversation,
                messageBody, "I had trouble executing that action. Please try again or handle it manually.",
                cancellationToken);
            return true;
        }
    }

    private async Task<string> ExecuteEmailAction(
        FamilyMember member,
        EmailSuggestedAction action,
        string emailSubject,
        CancellationToken cancellationToken)
    {
        if (action.Type == "CalendarEvent")
        {
            var start = DateTimeOffset.UtcNow.AddDays(1); // default
            if (!string.IsNullOrEmpty(action.SuggestedDate) && DateTimeOffset.TryParse(action.SuggestedDate, out var parsedDate))
            {
                start = parsedDate;
            }
            if (!string.IsNullOrEmpty(action.SuggestedTime) && TimeSpan.TryParse(action.SuggestedTime, out var parsedTime))
            {
                start = start.Date + parsedTime;
                var tz = TimeZoneInfo.FindSystemTimeZoneById(member.Timezone);
                start = new DateTimeOffset(DateTime.SpecifyKind(start.DateTime, DateTimeKind.Unspecified), tz.GetUtcOffset(start.DateTime));
            }

            var calendarEvent = new CalendarEvent
            {
                Subject = action.Description,
                Start = start,
                End = start.AddHours(1)
            };

            await _calendarService.CreateEventAsync(calendarEvent, cancellationToken);
            return $"Done! Created calendar event: \"{action.Description}\" on {start:MMMM d, yyyy} at {start:h:mm tt} ✅";
        }

        if (action.Type == "Reminder")
        {
            var fireAt = DateTimeOffset.UtcNow.AddDays(1); // default
            if (!string.IsNullOrEmpty(action.SuggestedDate) && DateTimeOffset.TryParse(action.SuggestedDate, out var parsedDate))
            {
                fireAt = parsedDate;
            }
            if (!string.IsNullOrEmpty(action.SuggestedTime) && TimeSpan.TryParse(action.SuggestedTime, out var parsedTime))
            {
                fireAt = fireAt.Date + parsedTime;
                var tz = TimeZoneInfo.FindSystemTimeZoneById(member.Timezone);
                fireAt = new DateTimeOffset(DateTime.SpecifyKind(fireAt.DateTime, DateTimeKind.Unspecified), tz.GetUtcOffset(fireAt.DateTime));
            }

            await _reminderService.CreateReminderAsync(
                member.PhoneNumber,
                $"Email reminder: {action.Description} (from: {emailSubject})",
                fireAt,
                cancellationToken);

            return $"Done! Set a reminder: \"{action.Description}\" for {fireAt:MMMM d, yyyy} at {fireAt:h:mm tt} ✅";
        }

        return $"Completed action: {action.Description}";
    }

    public async Task HandleInboundEmailAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing inbound email {MessageId}", messageId);
        await Task.CompletedTask;
    }

    private async Task TryCompressSessionAsync(ConversationHistory conversation, string memberName, CancellationToken cancellationToken)
    {
        if (conversation.CurrentSessionMessages.Count == 0)
            return;

        var gap = DateTime.UtcNow - conversation.LastMessageAt;
        if (gap.TotalHours < _assistantOptions.SessionBoundaryHours)
            return;

        _logger.LogInformation("Session boundary detected ({GapHours:F1}h gap), compressing {Count} messages",
            gap.TotalHours, conversation.CurrentSessionMessages.Count);

        try
        {
            var summary = await _sessionCompressionService.CompressSessionAsync(
                conversation.CurrentSessionMessages, memberName, cancellationToken);

            conversation.SessionSummaries.Insert(0, new SessionSummary
            {
                Summary = summary,
                SessionDate = conversation.LastMessageAt,
                MessageCount = conversation.CurrentSessionMessages.Count
            });

            var maxSummaries = _assistantOptions.MaxSessionSummaries;
            if (conversation.SessionSummaries.Count > maxSummaries)
            {
                conversation.SessionSummaries = conversation.SessionSummaries
                    .Take(maxSummaries).ToList();
            }

            conversation.CurrentSessionMessages.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session compression failed, continuing with uncompressed history");
        }
    }

    private async Task DetectAndSavePreferenceAsync(FamilyMember member, string userMessage, string assistantResponse, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _preferenceDetectionService.DetectPreferenceAsync(userMessage, assistantResponse, cancellationToken);
            if (result == null || !result.PreferenceDetected)
                return;

            var prefs = await _memberPreferencesRepository.GetAsync(member.PhoneNumber, cancellationToken)
                ?? new MemberPreferences
                {
                    Id = $"prefs-{member.PhoneNumber}",
                    PhoneNumber = member.PhoneNumber,
                    MemberName = member.Name
                };

            var updated = false;

            if (!string.IsNullOrEmpty(result.Field) && !string.IsNullOrEmpty(result.Value))
            {
                switch (result.Field.ToLowerInvariant())
                {
                    case "briefinglength":
                        prefs.BriefingLength = result.Value;
                        updated = true;
                        break;
                    case "communicationstyle":
                        prefs.CommunicationStyle = result.Value;
                        updated = true;
                        break;
                    case "quiethoursstart":
                        prefs.QuietHoursStart = result.Value;
                        updated = true;
                        break;
                    case "quiethoursend":
                        prefs.QuietHoursEnd = result.Value;
                        updated = true;
                        break;
                    case "defaultreminderleadtimehours":
                        if (int.TryParse(result.Value, out var hours))
                        {
                            prefs.DefaultReminderLeadTimeHours = hours;
                            updated = true;
                        }
                        break;
                }
            }

            if (!string.IsNullOrEmpty(result.LearnedKey) && !string.IsNullOrEmpty(result.LearnedValue))
            {
                prefs.LearnedPreferences[result.LearnedKey] = result.LearnedValue;
                updated = true;
            }

            if (updated)
            {
                prefs.UpdatedAt = DateTime.UtcNow;
                await _memberPreferencesRepository.UpsertAsync(prefs, cancellationToken);
                _logger.LogInformation("Auto-detected preference for {MemberName}: {Field}/{Key}",
                    member.Name, result.Field ?? result.LearnedKey, result.Value ?? result.LearnedValue);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preference detection failed for {MemberName}, continuing", member.Name);
        }
    }
}
