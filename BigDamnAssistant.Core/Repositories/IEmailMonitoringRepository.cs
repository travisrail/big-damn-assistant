using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IEmailMonitoringRepository
{
    Task<IReadOnlyList<MonitoredMailbox>> GetActiveMailboxesAsync(CancellationToken cancellationToken = default);
    Task UpsertMailboxAsync(MonitoredMailbox mailbox, CancellationToken cancellationToken = default);
    Task<bool> RemoveMailboxAsync(string emailAddress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WhitelistedSender>> GetActiveSendersAsync(CancellationToken cancellationToken = default);
    Task UpsertSenderAsync(WhitelistedSender sender, CancellationToken cancellationToken = default);
    Task<bool> RemoveSenderAsync(string emailAddress, CancellationToken cancellationToken = default);

    Task<MailboxScanState?> GetScanStateAsync(string emailAddress, CancellationToken cancellationToken = default);
    Task UpsertScanStateAsync(MailboxScanState state, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailActionPending>> GetUnresolvedActionsAsync(CancellationToken cancellationToken = default);
    Task UpsertActionAsync(EmailActionPending action, CancellationToken cancellationToken = default);
}
