using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IReminderRepository
{
    Task CreateAsync(ReminderDocument reminder, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReminderDocument>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string reminderId, CancellationToken cancellationToken = default);
}
