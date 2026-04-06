using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IFeatureRequestRepository
{
    Task<IReadOnlyList<FeatureRequest>> GetActiveRequestsAsync(CancellationToken cancellationToken = default);
    Task AddRequestAsync(FeatureRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveRequestAsync(string id, CancellationToken cancellationToken = default);
}
