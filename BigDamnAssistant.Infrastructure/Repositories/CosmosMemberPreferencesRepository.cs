using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosMemberPreferencesRepository : IMemberPreferencesRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosMemberPreferencesRepository> _logger;

    public CosmosMemberPreferencesRepository(Container container, ILogger<CosmosMemberPreferencesRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<MemberPreferences?> GetAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var id = $"prefs-{phoneNumber}";
        var partitionKey = new PartitionKey("preferences");

        try
        {
            var response = await _container.ReadItemAsync<MemberPreferences>(id, partitionKey, cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to read preferences for {Id}", id);
            throw;
        }
    }

    public async Task UpsertAsync(MemberPreferences preferences, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(preferences, new PartitionKey(preferences.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert preferences {Id}", preferences.Id);
            throw;
        }
    }
}
