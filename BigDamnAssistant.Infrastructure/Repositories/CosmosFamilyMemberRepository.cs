using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosFamilyMemberRepository : IFamilyMemberRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosFamilyMemberRepository> _logger;

    public CosmosFamilyMemberRepository(Container container, ILogger<CosmosFamilyMemberRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<FamilyMember?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var id = $"member-{phoneNumber}";
        var partitionKey = new PartitionKey("members");

        try
        {
            var response = await _container.ReadItemAsync<FamilyMember>(id, partitionKey, cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to read family member {Id}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<FamilyMember>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'familyMember'");
        var iterator = _container.GetItemQueryIterator<FamilyMember>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey("members")
        });

        var results = new List<FamilyMember>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertAsync(FamilyMember member, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(member, new PartitionKey(member.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert family member {Id}", member.Id);
            throw;
        }
    }
}
