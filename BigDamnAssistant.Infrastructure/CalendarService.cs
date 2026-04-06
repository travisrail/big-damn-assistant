using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace BigDamnAssistant.Infrastructure;

public class CalendarService : ICalendarService
{
    private readonly GraphServiceClient _graphClient;
    private readonly string _userId;
    private readonly ILogger<CalendarService> _logger;

    public CalendarService(GraphServiceClient graphClient, string userId, ILogger<CalendarService> logger)
    {
        _graphClient = graphClient;
        _userId = userId;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching calendar events for user {UserId} from {From} to {To}", _userId, from, to);
        try
        {
            var allEvents = new List<CalendarEvent>();

            var result = await _graphClient.Users[_userId].CalendarView
                .GetAsync(config =>
                {
                    config.QueryParameters.StartDateTime = from.ToString("O");
                    config.QueryParameters.EndDateTime = to.ToString("O");
                    config.QueryParameters.Select = new[] { "id", "subject", "start", "end", "location", "bodyPreview", "attendees" };
                    config.QueryParameters.Orderby = new[] { "start/dateTime" };
                    config.QueryParameters.Top = 250;
                }, cancellationToken);

            while (result?.Value != null)
            {
                allEvents.AddRange(result.Value.Select(e => new CalendarEvent
                {
                    Id = e.Id,
                    Subject = e.Subject ?? string.Empty,
                    Start = ParseGraphDateTime(e.Start),
                    End = ParseGraphDateTime(e.End),
                    Location = e.Location?.DisplayName,
                    Body = e.BodyPreview,
                    Attendees = e.Attendees?.Select(a => a.EmailAddress?.Address ?? "").Where(a => !string.IsNullOrEmpty(a)).ToList() ?? new()
                }));

                if (!string.IsNullOrEmpty(result.OdataNextLink))
                {
                    result = await _graphClient.Users[_userId].CalendarView
                        .WithUrl(result.OdataNextLink)
                        .GetAsync(cancellationToken: cancellationToken);
                }
                else
                {
                    break;
                }
            }

            return allEvents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get calendar events for user {UserId}: {Message}", _userId, ex.Message);
            throw;
        }
    }

    public async Task<CalendarEvent> CreateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var startUtc = calendarEvent.Start.UtcDateTime;
            var endUtc = calendarEvent.End.UtcDateTime;

            var graphEvent = new Event
            {
                Subject = calendarEvent.Subject,
                Start = new DateTimeTimeZone { DateTime = startUtc.ToString("yyyy-MM-ddTHH:mm:ss"), TimeZone = "UTC" },
                End = new DateTimeTimeZone { DateTime = endUtc.ToString("yyyy-MM-ddTHH:mm:ss"), TimeZone = "UTC" },
                Location = calendarEvent.Location is not null ? new Location { DisplayName = calendarEvent.Location } : null
            };

            if (calendarEvent.Attendees.Count > 0)
            {
                graphEvent.Attendees = calendarEvent.Attendees.Select(email => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = email },
                    Type = AttendeeType.Required
                }).ToList();
            }

            var created = await _graphClient.Users[_userId].Events.PostAsync(graphEvent, cancellationToken: cancellationToken);

            calendarEvent.Id = created?.Id;
            return calendarEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create calendar event for user {UserId}: {Message}", _userId, ex.Message);
            throw;
        }
    }

    public async Task<CalendarEvent> UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var startUtc = calendarEvent.Start.UtcDateTime;
            var endUtc = calendarEvent.End.UtcDateTime;

            var graphEvent = new Event
            {
                Subject = calendarEvent.Subject,
                Start = new DateTimeTimeZone { DateTime = startUtc.ToString("yyyy-MM-ddTHH:mm:ss"), TimeZone = "UTC" },
                End = new DateTimeTimeZone { DateTime = endUtc.ToString("yyyy-MM-ddTHH:mm:ss"), TimeZone = "UTC" },
                Location = calendarEvent.Location is not null ? new Location { DisplayName = calendarEvent.Location } : null
            };

            await _graphClient.Users[_userId].Events[calendarEvent.Id].PatchAsync(graphEvent, cancellationToken: cancellationToken);
            return calendarEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update calendar event {EventId}", calendarEvent.Id);
            throw;
        }
    }

    public async Task CancelEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Cancelling calendar event {EventId}", eventId);
            await _graphClient.Users[_userId].Events[eventId].DeleteAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel calendar event {EventId}: {Message}", eventId, ex.Message);
            throw;
        }
    }

    private static DateTimeOffset ParseGraphDateTime(DateTimeTimeZone? graphDateTime)
    {
        if (graphDateTime?.DateTime is null)
            return DateTimeOffset.MinValue;

        var dt = DateTime.SpecifyKind(DateTime.Parse(graphDateTime.DateTime), DateTimeKind.Unspecified);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(graphDateTime.TimeZone ?? "UTC");
        var localDto = new DateTimeOffset(dt, tz.GetUtcOffset(dt));
        return localDto.ToUniversalTime();
    }
}
