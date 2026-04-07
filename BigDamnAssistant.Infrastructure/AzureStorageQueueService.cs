using System.Text.Json;
using Azure.Storage.Queues;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class AzureStorageQueueService : IQueueService
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly ILogger<AzureStorageQueueService> _logger;

    public AzureStorageQueueService(QueueServiceClient queueServiceClient, ILogger<AzureStorageQueueService> logger)
    {
        _queueServiceClient = queueServiceClient;
        _logger = logger;
    }

    public async Task EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default)
    {
        var queueClient = _queueServiceClient.GetQueueClient(queueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(message);
        // Azure Storage Queue expects base64-encoded messages when using the SDK
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        await queueClient.SendMessageAsync(base64, cancellationToken: cancellationToken);

        _logger.LogDebug("Enqueued message to {QueueName}", queueName);
    }
}
