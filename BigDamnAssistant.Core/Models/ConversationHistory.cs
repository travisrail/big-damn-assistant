namespace BigDamnAssistant.Core.Models;

public class ConversationHistory
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string Type { get; set; } = "conversation";
    public string PhoneNumber { get; set; } = string.Empty;

    // Legacy field — present on old documents, used for migration only
    public List<ConversationMessage>? Messages { get; set; }

    // Short-term: raw messages from current session only
    public List<ConversationMessage> CurrentSessionMessages { get; set; } = new();

    // Medium-term: compressed summaries of previous sessions
    public List<SessionSummary> SessionSummaries { get; set; } = new();

    public PendingAction? PendingAction { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public const int DefaultMaxSessionMessages = 10;
    public const int DefaultMaxSessionSummaries = 5;

    public void MigrateIfNeeded()
    {
        if (Messages != null && Messages.Count > 0 && CurrentSessionMessages.Count == 0)
        {
            CurrentSessionMessages = new List<ConversationMessage>(Messages);
            Messages = null;
        }
        else if (Messages != null && Messages.Count == 0)
        {
            Messages = null;
        }
    }

    public void AddMessage(string role, string content, int maxMessages = DefaultMaxSessionMessages)
    {
        CurrentSessionMessages.Add(new ConversationMessage { Role = role, Content = content });

        if (CurrentSessionMessages.Count > maxMessages)
        {
            CurrentSessionMessages = CurrentSessionMessages
                .Skip(CurrentSessionMessages.Count - maxMessages)
                .ToList();
        }

        LastMessageAt = DateTime.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
