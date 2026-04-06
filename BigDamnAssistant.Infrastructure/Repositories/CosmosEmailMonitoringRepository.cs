using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure.Repositories;

public class CosmosEmailMonitoringRepository : IEmailMonitoringRepository
{
    private static readonly PartitionKey EmailMonitoringPartition = new("emailMonitoring");

    private readonly Container _container;
    private readonly ILogger<CosmosEmailMonitoringRepository> _logger;

    public CosmosEmailMonitoringRepository(Container container, ILogger<CosmosEmailMonitoringRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    #region Mailboxes

    public async Task<IReadOnlyList<MonitoredMailbox>> GetActiveMailboxesAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'monitoredMailbox' AND c.active = true");
        var iterator = _container.GetItemQueryIterator<MonitoredMailbox>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = EmailMonitoringPartition
        });

        var results = new List<MonitoredMailbox>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertMailboxAsync(MonitoredMailbox mailbox, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(mailbox, new PartitionKey(mailbox.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert monitored mailbox {Id}", mailbox.Id);
            throw;
        }
    }

    public async Task<bool> RemoveMailboxAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'monitoredMailbox' AND c.emailAddress = @email")
            .WithParameter("@email", emailAddress);

        var iterator = _container.GetItemQueryIterator<MonitoredMailbox>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = EmailMonitoringPartition
        });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var mailbox in response)
            {
                mailbox.Active = false;
                await _container.UpsertItemAsync(mailbox, EmailMonitoringPartition, cancellationToken: cancellationToken);
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Senders

    public async Task<IReadOnlyList<WhitelistedSender>> GetActiveSendersAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'whitelistedSender' AND c.active = true");
        var iterator = _container.GetItemQueryIterator<WhitelistedSender>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = EmailMonitoringPartition
        });

        var results = new List<WhitelistedSender>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertSenderAsync(WhitelistedSender sender, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(sender, new PartitionKey(sender.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert whitelisted sender {Id}", sender.Id);
            throw;
        }
    }

    public async Task<bool> RemoveSenderAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'whitelistedSender' AND c.emailAddress = @email")
            .WithParameter("@email", emailAddress);

        var iterator = _container.GetItemQueryIterator<WhitelistedSender>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = EmailMonitoringPartition
        });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var sender in response)
            {
                sender.Active = false;
                await _container.UpsertItemAsync(sender, EmailMonitoringPartition, cancellationToken: cancellationToken);
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Scan State

    public async Task<MailboxScanState?> GetScanStateAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var id = $"scanstate-{emailAddress}";

        try
        {
            var response = await _container.ReadItemAsync<MailboxScanState>(id, EmailMonitoringPartition, cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to read scan state for {Email}", emailAddress);
            throw;
        }
    }

    public async Task UpsertScanStateAsync(MailboxScanState state, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(state, new PartitionKey(state.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert scan state {Id}", state.Id);
            throw;
        }
    }

    #endregion

    #region Pending Actions

    public async Task<IReadOnlyList<EmailActionPending>> GetUnresolvedActionsAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'emailActionPending' AND c.resolvedBy = null");
        var iterator = _container.GetItemQueryIterator<EmailActionPending>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = EmailMonitoringPartition
        });

        var results = new List<EmailActionPending>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task UpsertActionAsync(EmailActionPending action, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.UpsertItemAsync(action, new PartitionKey(action.PartitionKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to upsert email action {Id}", action.Id);
            throw;
        }
    }

    #endregion
}
