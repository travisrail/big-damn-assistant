namespace BigDamnAssistant.Core.Services;

public interface IQueueService
{
    Task EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default);
}
