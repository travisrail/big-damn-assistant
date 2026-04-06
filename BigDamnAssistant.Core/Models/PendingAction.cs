namespace BigDamnAssistant.Core.Models;

public class PendingAction
{
    public string ActionType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
