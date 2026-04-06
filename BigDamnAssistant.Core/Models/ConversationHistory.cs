namespace BigDamnAssistant.Core.Models;

public class ConversationHistory
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string Type { get; set; } = "conversation";
    public string PhoneNumber { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
    public PendingAction? PendingAction { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public const int MaxMessages = 20;

    public void AddMessage(string role, string content)
    {
        Messages.Add(new ConversationMessage { Role = role, Content = content });

        if (Messages.Count > MaxMessages)
        {
            Messages = Messages.Skip(Messages.Count - MaxMessages).ToList();
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
