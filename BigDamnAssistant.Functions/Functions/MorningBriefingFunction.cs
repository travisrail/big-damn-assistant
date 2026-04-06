using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BigDamnAssistant.Functions.Functions;

public class MorningBriefingFunction
{
    private readonly ICalendarService _calendarService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IFamilyMemberRepository _familyMemberRepository;
    private readonly IAffirmationService _affirmationService;
    private readonly ILogger<MorningBriefingFunction> _logger;
    private readonly AssistantOptions _assistantOptions;

    public MorningBriefingFunction(
        ICalendarService calendarService,
        IWhatsAppService whatsAppService,
        IFamilyMemberRepository familyMemberRepository,
        IAffirmationService affirmationService,
        ILogger<MorningBriefingFunction> logger,
        IOptions<AssistantOptions> assistantOptions)
    {
        _calendarService = calendarService;
        _whatsAppService = whatsAppService;
        _familyMemberRepository = familyMemberRepository;
        _affirmationService = affirmationService;
        _logger = logger;
        _assistantOptions = assistantOptions.Value;
    }

    // Runs at 7:00 AM CT daily (12:00 UTC)
    [Function("MorningBriefing")]
    public async Task Run(
        [TimerTrigger("0 0 12 * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Morning briefing triggered");

        try
        {
            var members = await _familyMemberRepository.GetAllAsync(cancellationToken);
            if (members.Count == 0)
            {
                _logger.LogWarning("No family members found, skipping briefing");
                return;
            }

            // Advance affirmation rotation and get general affirmation
            var featuredMember = await _affirmationService.AdvanceRotationAsync(cancellationToken);
            var generalAffirmation = await _affirmationService.GetGeneralAffirmationAsync(cancellationToken);

            // Fetch events for the full day in Central Time so we get the correct local day
            var centralTz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
            var centralNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, centralTz);
            var todayLocalStart = new DateTimeOffset(centralNow.Date, centralTz.GetUtcOffset(centralNow.Date));
            var todayLocalEnd = todayLocalStart.AddDays(1);
            var events = await _calendarService.GetEventsAsync(
                todayLocalStart, todayLocalEnd, cancellationToken);

            _logger.LogInformation("Found {EventCount} events for today's briefing", events.Count);

            foreach (var member in members)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(member.Timezone);
                    var briefing = BuildBriefing(events, tz);

                    if (member.Id == featuredMember?.Id)
                    {
                        var affirmation = await _affirmationService.GetPersonalizedAffirmationAsync(member, cancellationToken);
                        briefing += $"\n\n⭐ Today's special affirmation is just for you, {member.Name}:\n{affirmation}";
                    }
                    else
                    {
                        briefing += $"\n\n💛 Today's affirmation: {generalAffirmation}";
                    }

                    await _whatsAppService.SendMessageAsync(member.PhoneNumber, briefing, cancellationToken);
                    _logger.LogInformation("Morning briefing sent to {MemberName}", member.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send morning briefing to {MemberName}: {Message}", member.Name, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run morning briefing");
        }
    }

    private string BuildBriefing(IReadOnlyList<CalendarEvent> events, TimeZoneInfo memberTz)
    {
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, memberTz);
        var briefing = $"Good morning from {_assistantOptions.Name}! ☀️\n\n";
        briefing += $"Here's your day — {localDate:dddd, MMMM d}:\n\n";

        // Filter to events that actually occur today in the member's timezone
        var todayStart = new DateTimeOffset(localDate.Date, memberTz.GetUtcOffset(localDate.Date));
        var todayEnd = todayStart.AddDays(1);
        var todayEvents = events
            .Where(e => e.End > todayStart && e.Start < todayEnd)
            .ToList();

        if (todayEvents.Count == 0)
        {
            briefing += "No events on the calendar today. Enjoy your free day!";
            return briefing;
        }

        foreach (var evt in todayEvents.OrderBy(e => e.Start))
        {
            var localStart = TimeZoneInfo.ConvertTime(evt.Start, memberTz);
            var localEnd = TimeZoneInfo.ConvertTime(evt.End, memberTz);
            var time = $"{localStart:h:mm tt} – {localEnd:h:mm tt}";

            briefing += $"• {time}: {evt.Subject}";
            if (!string.IsNullOrEmpty(evt.Location))
                briefing += $"\n  📍 {evt.Location}";
            briefing += "\n\n";
        }

        briefing += $"Have a great day! Reply anytime if you need me.";
        return briefing;
    }
}
