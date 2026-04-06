using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace BigDamnAssistant.Functions.Functions;

public class GraphSubscriptionRenewalFunction
{
    private readonly GraphServiceClient _graphClient;
    private readonly IGraphSubscriptionRepository _subscriptionRepository;
    private readonly ILogger<GraphSubscriptionRenewalFunction> _logger;

    public GraphSubscriptionRenewalFunction(
        GraphServiceClient graphClient,
        IGraphSubscriptionRepository subscriptionRepository,
        ILogger<GraphSubscriptionRenewalFunction> logger)
    {
        _graphClient = graphClient;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    // Runs every 2 days to renew Graph subscriptions (they expire every 3 days)
    [Function("GraphSubscriptionRenewal")]
    public async Task Run(
        [TimerTrigger("0 0 0 */2 * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graph subscription renewal triggered");

        try
        {
            var subDoc = await _subscriptionRepository.GetAsync("graphsub-mail", cancellationToken);

            if (subDoc is not null && !string.IsNullOrEmpty(subDoc.SubscriptionId))
            {
                // Renew existing subscription
                var newExpiry = DateTimeOffset.UtcNow.AddDays(3);
                await _graphClient.Subscriptions[subDoc.SubscriptionId].PatchAsync(
                    new Subscription { ExpirationDateTime = newExpiry },
                    cancellationToken: cancellationToken);

                subDoc.ExpiresAt = newExpiry;
                await _subscriptionRepository.UpsertAsync(subDoc, cancellationToken);

                _logger.LogInformation("Renewed Graph mail subscription until {ExpiresAt}", newExpiry);
            }
            else
            {
                // Create new subscription
                var subscription = await _graphClient.Subscriptions.PostAsync(new Subscription
                {
                    ChangeType = "created",
                    NotificationUrl = Environment.GetEnvironmentVariable("GraphWebhookUrl")
                        ?? throw new InvalidOperationException("GraphWebhookUrl not configured"),
                    Resource = "me/mailFolders/inbox/messages",
                    ExpirationDateTime = DateTimeOffset.UtcNow.AddDays(3),
                    ClientState = Guid.NewGuid().ToString()
                }, cancellationToken: cancellationToken);

                if (subscription is not null)
                {
                    var doc = new GraphSubscriptionDocument
                    {
                        Id = "graphsub-mail",
                        SubscriptionId = subscription.Id ?? string.Empty,
                        ExpiresAt = subscription.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddDays(3),
                        Resource = "me/mailFolders/inbox/messages"
                    };
                    await _subscriptionRepository.UpsertAsync(doc, cancellationToken);

                    _logger.LogInformation("Created new Graph mail subscription {SubscriptionId}", subscription.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew/create Graph subscription");
        }
    }
}
