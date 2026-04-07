using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosProcessedMessageRepository> _logger;

    public CosmosProcessedMessageRepository(Container container, ILogger<CosmosProcessedMessageRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<bool> ExistsAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = $"processed-{messageSid}";
            await _container.ReadItemAsync<ProcessedMessage>(id, new PartitionKey("processedMessages"), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task CreateAsync(ProcessedMessage document, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.CreateItemAsync(document, new PartitionKey(document.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Already exists — another instance processed it first, which is fine
            _logger.LogDebug("Processed message document already exists for {MessageSid}", document.MessageSid);
        }
    }
}
