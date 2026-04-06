namespace BigDamnAssistant.Core.Models;

public class MailboxScanState
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "emailMonitoring";
    public string Type { get; set; } = "mailboxScanState";
    public string EmailAddress { get; set; } = string.Empty;
    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
    public string LastProcessedEmailId { get; set; } = string.Empty;
}
