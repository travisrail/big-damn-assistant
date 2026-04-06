using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IReminderService
{
    Task CreateReminderAsync(
        string targetPhoneNumber,
        string message,
        DateTimeOffset fireAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReminderDocument>> GetDueRemindersAsync(CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(string reminderId, CancellationToken cancellationToken = default);
}
