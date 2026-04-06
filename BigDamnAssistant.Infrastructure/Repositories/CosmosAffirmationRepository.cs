using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosAffirmationRepository : IAffirmationRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosAffirmationRepository> _logger;

    public CosmosAffirmationRepository(Container container, ILogger<CosmosAffirmationRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AffirmationPoolItem>> GetActiveAffirmationsAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'affirmationPool' AND c.active = true");
        var iterator = _container.GetItemQueryIterator<AffirmationPoolItem>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey("affirmations")
        });

        var results = new List<AffirmationPoolItem>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertAffirmationAsync(AffirmationPoolItem affirmation, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(affirmation, new PartitionKey(affirmation.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert affirmation {Id}", affirmation.Id);
            throw;
        }
    }

    public async Task<bool> RemoveAffirmationAsync(string text, CancellationToken cancellationToken = default)
    {
        var affirmations = await GetActiveAffirmationsAsync(cancellationToken);
        var match = affirmations.FirstOrDefault(a => a.Text.Equals(text, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return false;

        match.Active = false;
        await UpsertAffirmationAsync(match, cancellationToken);
        return true;
    }

    public async Task<AffirmationRotation?> GetRotationStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<AffirmationRotation>(
                "affirmation-rotation",
                new PartitionKey("system"),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to read affirmation rotation state");
            throw;
        }
    }

    public async Task UpsertRotationStateAsync(AffirmationRotation rotation, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(rotation, new PartitionKey("system"), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert affirmation rotation state");
            throw;
        }
    }
}
