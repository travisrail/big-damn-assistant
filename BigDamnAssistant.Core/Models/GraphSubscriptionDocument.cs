namespace BigDamnAssistant.Core.Models;

public class GraphSubscriptionDocument
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "system";
    public string Type { get; set; } = "graphSubscription";
    public string SubscriptionId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Resource { get; set; } = string.Empty;
}
