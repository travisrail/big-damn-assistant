namespace BigDamnAssistant.Core.Models;

public class WhitelistedSender
{
    public string Id { get; set; } = $"sender-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "emailMonitoring";
    public string Type { get; set; } = "whitelistedSender";
    public string EmailAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; } = true;
}
