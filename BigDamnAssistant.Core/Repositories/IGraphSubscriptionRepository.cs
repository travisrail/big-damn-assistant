using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IGraphSubscriptionRepository
{
    Task<GraphSubscriptionDocument?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertAsync(GraphSubscriptionDocument subscription, CancellationToken cancellationToken = default);
}
