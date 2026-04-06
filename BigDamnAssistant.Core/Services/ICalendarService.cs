namespace BigDamnAssistant.Core.Services;

public class CalendarEvent
{
    public string? Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string? Location { get; set; }
    public string? Body { get; set; }
    public List<string> Attendees { get; set; } = new();
}

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<CalendarEvent> CreateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default);

    Task<CalendarEvent> UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default);

    Task CancelEventAsync(string eventId, CancellationToken cancellationToken = default);
}
