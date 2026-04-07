namespace BigDamnAssistant.Core.Models;

public class ProcessedMessage
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "processedMessages";
    public string Type { get; set; } = "processedMessage";
    public string MessageSid { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public int Ttl { get; set; } = 86400;
}
