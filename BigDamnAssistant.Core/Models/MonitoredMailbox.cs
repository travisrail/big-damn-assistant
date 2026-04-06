namespace BigDamnAssistant.Core.Models;

public class MonitoredMailbox
{
    public string Id { get; set; } = $"mailbox-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "emailMonitoring";
    public string Type { get; set; } = "monitoredMailbox";
    public string EmailAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; } = true;
}
