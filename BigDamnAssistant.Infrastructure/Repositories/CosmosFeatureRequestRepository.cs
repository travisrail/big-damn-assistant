using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosFeatureRequestRepository : IFeatureRequestRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosFeatureRequestRepository> _logger;

    public CosmosFeatureRequestRepository(Container container, ILogger<CosmosFeatureRequestRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FeatureRequest>> GetActiveRequestsAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'featureRequest' AND c.active = true");
        var iterator = _container.GetItemQueryIterator<FeatureRequest>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey("featureRequests")
        });

        var results = new List<FeatureRequest>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task AddRequestAsync(FeatureRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.CreateItemAsync(request, new PartitionKey(request.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to add feature request {Id}", request.Id);
            throw;
        }
    }

    public async Task<bool> RemoveRequestAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<FeatureRequest>(id, new PartitionKey("featureRequests"), cancellationToken: cancellationToken);
            var item = response.Resource;
            item.Active = false;
            await _container.UpsertItemAsync(item, new PartitionKey("featureRequests"), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to remove feature request {Id}", id);
            throw;
        }
    }
}
