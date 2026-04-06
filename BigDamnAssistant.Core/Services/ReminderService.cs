using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;

namespace BigDamnAssistant.Core.Services;

public class ReminderService : IReminderService
{
    private readonly IReminderRepository _reminderRepository;

    public ReminderService(IReminderRepository reminderRepository)
    {
        _reminderRepository = reminderRepository;
    }

    public async Task CreateReminderAsync(
        string targetPhoneNumber,
        string message,
        DateTimeOffset fireAt,
        CancellationToken cancellationToken = default)
    {
        var reminder = new ReminderDocument
        {
            Id = $"reminder-{Guid.NewGuid()}",
            TargetPhoneNumber = targetPhoneNumber,
            Message = message,
            FireAt = fireAt,
            Processed = false
        };

        await _reminderRepository.CreateAsync(reminder, cancellationToken);
    }

    public Task<IReadOnlyList<ReminderDocument>> GetDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        return _reminderRepository.GetPendingRemindersAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    public Task MarkProcessedAsync(string reminderId, CancellationToken cancellationToken = default)
    {
        return _reminderRepository.MarkProcessedAsync(reminderId, cancellationToken);
    }
}
