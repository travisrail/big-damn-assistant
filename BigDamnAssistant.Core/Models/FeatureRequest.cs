namespace BigDamnAssistant.Core.Models;

public class FeatureRequest
{
    public string Id { get; set; } = $"featurerequest-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "featureRequests";
    public string Type { get; set; } = "featureRequest";
    public string Description { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; } = true;
}
