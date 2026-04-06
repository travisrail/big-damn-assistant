namespace BigDamnAssistant.Core.Models;

public class ReminderDocument
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "reminders";
    public string Type { get; set; } = "reminder";
    public string TargetPhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset FireAt { get; set; }
    public int Ttl { get; set; } = 86400;
    public bool Processed { get; set; }
}
