using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IProcessedMessageRepository
{
    Task<bool> ExistsAsync(string messageSid, CancellationToken cancellationToken = default);
    Task CreateAsync(ProcessedMessage document, CancellationToken cancellationToken = default);
}
