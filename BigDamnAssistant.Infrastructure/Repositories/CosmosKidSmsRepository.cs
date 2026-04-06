using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosKidSmsRepository : IKidSmsRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosKidSmsRepository> _logger;

    public CosmosKidSmsRepository(Container container, ILogger<CosmosKidSmsRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<KidSmsUser?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.partitionKey = 'kidSmsUsers' AND c.type = 'kidSmsUser' AND c.active = true AND (c.smsPhoneNumber = @phone OR c.whatsAppPhoneNumber = @phone)")
            .WithParameter("@phone", phoneNumber);

        using var iterator = _container.GetItemQueryIterator<KidSmsUser>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<KidSmsUser?> GetByProfileNameAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.partitionKey = 'kidSmsUsers' AND c.type = 'kidSmsUser' AND c.active = true AND LOWER(c.linkedProfileName) = LOWER(@name)")
            .WithParameter("@name", profileName);

        using var iterator = _container.GetItemQueryIterator<KidSmsUser>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<IReadOnlyList<KidSmsUser>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.partitionKey = 'kidSmsUsers' AND c.type = 'kidSmsUser' AND c.active = true");
        var results = new List<KidSmsUser>();

        using var iterator = _container.GetItemQueryIterator<KidSmsUser>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertAsync(KidSmsUser kid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(kid, new PartitionKey(kid.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert kid SMS user {Id}", kid.Id);
            throw;
        }
    }
}
