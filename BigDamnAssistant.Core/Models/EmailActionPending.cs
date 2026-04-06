namespace BigDamnAssistant.Core.Models;

public class EmailActionPending
{
    public string Id { get; set; } = $"emailaction-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "emailMonitoring";
    public string Type { get; set; } = "emailActionPending";
    public string EmailId { get; set; } = string.Empty;
    public string EmailSubject { get; set; } = string.Empty;
    public string SourceMailbox { get; set; } = string.Empty;
    public List<EmailSuggestedAction> SuggestedActions { get; set; } = new();
    public DateTime NotifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int Ttl { get; set; } = 86400;
}

public class EmailSuggestedAction
{
    public string Type { get; set; } = string.Empty; // "CalendarEvent" or "Reminder"
    public string Description { get; set; } = string.Empty;
    public string? SuggestedDate { get; set; }
    public string? SuggestedTime { get; set; }
}

public class EmailAnalysisResult
{
    public bool IsActionable { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<EmailSuggestedAction> SuggestedActions { get; set; } = new();
}
