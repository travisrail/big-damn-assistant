using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosFamilyMemoryRepository : IFamilyMemoryRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosFamilyMemoryRepository> _logger;

    public CosmosFamilyMemoryRepository(Container container, ILogger<CosmosFamilyMemoryRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FamilyMemory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'familyMemory'");
        var iterator = _container.GetItemQueryIterator<FamilyMemory>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey("familyMemory")
        });

        var results = new List<FamilyMemory>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertAsync(FamilyMemory memory, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(memory, new PartitionKey(memory.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert family memory {Id}", memory.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<FamilyMemory>(id, new PartitionKey("familyMemory"), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Attempted to delete non-existent memory {Id}", id);
            return false;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to delete family memory {Id}", id);
            throw;
        }
    }
}
