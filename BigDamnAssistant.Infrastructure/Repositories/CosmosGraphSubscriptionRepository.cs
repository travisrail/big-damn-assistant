using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosGraphSubscriptionRepository : IGraphSubscriptionRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosGraphSubscriptionRepository> _logger;

    public CosmosGraphSubscriptionRepository(Container container, ILogger<CosmosGraphSubscriptionRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<GraphSubscriptionDocument?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<GraphSubscriptionDocument>(
                id, new PartitionKey("system"), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to read graph subscription {Id}", id);
            throw;
        }
    }

    public async Task UpsertAsync(GraphSubscriptionDocument subscription, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(subscription, new PartitionKey(subscription.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert graph subscription {Id}", subscription.Id);
            throw;
        }
    }
}
