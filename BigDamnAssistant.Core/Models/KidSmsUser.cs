namespace BigDamnAssistant.Core.Models;

public class KidSmsUser
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "kidSmsUsers";
    public string Type { get; set; } = "kidSmsUser";
    public string? SmsPhoneNumber { get; set; }
    public string? WhatsAppPhoneNumber { get; set; }
    public string PreferredChannel { get; set; } = "SMS";
    public string LinkedProfileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string AddedBy { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
