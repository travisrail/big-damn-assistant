namespace BigDamnAssistant.Core.Models;

public class MemberPreferences
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "preferences";
    public string Type { get; set; } = "memberPreferences";
    public string PhoneNumber { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;

    // Communication
    public string BriefingLength { get; set; } = "normal";
    public string CommunicationStyle { get; set; } = "casual";
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }

    // Content preferences
    public List<string> TopicsOfInterest { get; set; } = new();
    public List<string> TopicsToAvoid { get; set; } = new();

    // Reminder preferences
    public int DefaultReminderLeadTimeHours { get; set; } = 24;

    // Free-form learned preferences
    public Dictionary<string, string> LearnedPreferences { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
