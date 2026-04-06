using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosFamilyProfileRepository : IFamilyProfileRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosFamilyProfileRepository> _logger;

    public CosmosFamilyProfileRepository(Container container, ILogger<CosmosFamilyProfileRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FamilyProfile>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.partitionKey = 'familyProfiles' AND c.type = 'familyProfile' AND c.active = true");
        var results = new List<FamilyProfile>();

        using var iterator = _container.GetItemQueryIterator<FamilyProfile>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<FamilyProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.partitionKey = 'familyProfiles' AND c.type = 'familyProfile' AND c.active = true AND (LOWER(c.name) = LOWER(@name) OR ARRAY_CONTAINS(c.nicknames, @name, true))")
            .WithParameter("@name", name);

        using var iterator = _container.GetItemQueryIterator<FamilyProfile>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task UpsertAsync(FamilyProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(profile, new PartitionKey(profile.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert family profile {Id}", profile.Id);
            throw;
        }
    }

    public async Task<bool> DeactivateAsync(string name, CancellationToken cancellationToken = default)
    {
        var profile = await GetByNameAsync(name, cancellationToken);
        if (profile == null) return false;

        profile.Active = false;
        profile.UpdatedAt = DateTime.UtcNow;
        await UpsertAsync(profile, cancellationToken);
        return true;
    }
}
