using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosReminderRepository : IReminderRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosReminderRepository> _logger;

    public CosmosReminderRepository(Container container, ILogger<CosmosReminderRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task CreateAsync(ReminderDocument reminder, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.CreateItemAsync(reminder, new PartitionKey(reminder.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to create reminder {Id}", reminder.Id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ReminderDocument>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = 'reminder' AND c.processed = false AND c.fireAt <= @now")
            .WithParameter("@now", asOf.ToString("O"));

        var iterator = _container.GetItemQueryIterator<ReminderDocument>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey("reminders")
        });

        var results = new List<ReminderDocument>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task MarkProcessedAsync(string reminderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var operations = new List<PatchOperation>
            {
                PatchOperation.Set("/processed", true)
            };

            await _container.PatchItemAsync<ReminderDocument>(
                reminderId,
                new PartitionKey("reminders"),
                operations,
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to mark reminder {Id} as processed", reminderId);
            throw;
        }
    }
}
