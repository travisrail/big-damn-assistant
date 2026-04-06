using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BigDamnAssistant.Functions.Functions;

public class WeeklyRecapFunction
{
    private readonly ICalendarService _calendarService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IFamilyMemberRepository _familyMemberRepository;
    private readonly ILogger<WeeklyRecapFunction> _logger;
    private readonly AssistantOptions _assistantOptions;

    public WeeklyRecapFunction(
        ICalendarService calendarService,
        IWhatsAppService whatsAppService,
        IFamilyMemberRepository familyMemberRepository,
        ILogger<WeeklyRecapFunction> logger,
        IOptions<AssistantOptions> assistantOptions)
    {
        _calendarService = calendarService;
        _whatsAppService = whatsAppService;
        _familyMemberRepository = familyMemberRepository;
        _logger = logger;
        _assistantOptions = assistantOptions.Value;
    }

    // Runs Sunday at 6:00 PM CT
    [Function("WeeklyRecap")]
    public async Task Run(
        [TimerTrigger("0 0 23 * * 0")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Weekly recap triggered");

        try
        {
            var startOfWeek = DateTimeOffset.UtcNow.Date.AddDays(1); // Monday
            var endOfWeek = startOfWeek.AddDays(7);

            var events = await _calendarService.GetEventsAsync(startOfWeek, endOfWeek, cancellationToken);

            var recap = $"Here's your week ahead from {_assistantOptions.Name}:\n\n";
            if (events.Count == 0)
            {
                recap += "No events scheduled — enjoy the open week!";
            }
            else
            {
                var grouped = events.GroupBy(e => e.Start.Date);
                foreach (var day in grouped)
                {
                    recap += $"**{day.Key:dddd, MMM d}**\n";
                    foreach (var evt in day)
                    {
                        recap += $"  - {evt.Start:h:mm tt}: {evt.Subject}\n";
                    }
                    recap += "\n";
                }
            }

            var members = await _familyMemberRepository.GetAllAsync(cancellationToken);
            foreach (var member in members)
            {
                await _whatsAppService.SendMessageAsync(member.PhoneNumber, recap, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send weekly recap");
        }
    }
}
