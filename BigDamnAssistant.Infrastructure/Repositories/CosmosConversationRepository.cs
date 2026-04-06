using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosConversationRepository : IConversationRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosConversationRepository> _logger;

    public CosmosConversationRepository(Container container, ILogger<CosmosConversationRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<ConversationHistory> GetOrCreateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var id = $"conv-{phoneNumber}";
        var partitionKey = new PartitionKey($"conv-{phoneNumber}");

        try
        {
            var response = await _container.ReadItemAsync<ConversationHistory>(id, partitionKey, cancellationToken: cancellationToken);
            var conversation = response.Resource;

            // Migrate legacy documents that use flat Messages array
            conversation.MigrateIfNeeded();

            return conversation;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ConversationHistory
            {
                Id = id,
                PartitionKey = $"conv-{phoneNumber}",
                PhoneNumber = phoneNumber
            };
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to read conversation for {Id}", id);
            throw;
        }
    }

    public async Task UpsertAsync(ConversationHistory conversation, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(conversation, new PartitionKey(conversation.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert conversation {Id}", conversation.Id);
            throw;
        }
    }
}
