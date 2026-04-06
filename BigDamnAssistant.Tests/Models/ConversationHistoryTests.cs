using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Tests.Models;

public class ConversationHistoryTests
{
    [Fact]
    public void AddMessage_AppendsToCurrentSessionMessages()
    {
        var history = new ConversationHistory();

        history.AddMessage("user", "Hello");

        Assert.Single(history.CurrentSessionMessages);
        Assert.Equal("user", history.CurrentSessionMessages[0].Role);
        Assert.Equal("Hello", history.CurrentSessionMessages[0].Content);
    }

    [Fact]
    public void AddMessage_TrimsOldMessages_WhenExceedingMax()
    {
        var history = new ConversationHistory();
        var max = ConversationHistory.DefaultMaxSessionMessages;

        for (int i = 0; i < max + 5; i++)
        {
            history.AddMessage("user", $"Message {i}");
        }

        Assert.Equal(max, history.CurrentSessionMessages.Count);
        Assert.Equal($"Message 5", history.CurrentSessionMessages[0].Content);
    }

    [Fact]
    public void AddMessage_RespectsCustomMaxMessages()
    {
        var history = new ConversationHistory();

        for (int i = 0; i < 10; i++)
        {
            history.AddMessage("user", $"Message {i}", maxMessages: 4);
        }

        Assert.Equal(4, history.CurrentSessionMessages.Count);
        Assert.Equal("Message 6", history.CurrentSessionMessages[0].Content);
    }

    [Fact]
    public void AddMessage_UpdatesTimestamps()
    {
        var history = new ConversationHistory();
        var before = DateTime.UtcNow;

        history.AddMessage("user", "Hello");

        Assert.True(history.LastMessageAt >= before);
        Assert.True(history.UpdatedAt >= before);
    }

    [Fact]
    public void MigrateIfNeeded_MovesLegacyMessagesToCurrentSession()
    {
        var history = new ConversationHistory
        {
            Messages = new List<ConversationMessage>
            {
                new() { Role = "user", Content = "Old message" },
                new() { Role = "assistant", Content = "Old response" }
            }
        };

        history.MigrateIfNeeded();

        Assert.Equal(2, history.CurrentSessionMessages.Count);
        Assert.Equal("Old message", history.CurrentSessionMessages[0].Content);
        Assert.Null(history.Messages);
    }

    [Fact]
    public void MigrateIfNeeded_DoesNotOverwriteExistingCurrentSession()
    {
        var history = new ConversationHistory
        {
            Messages = new List<ConversationMessage>
            {
                new() { Role = "user", Content = "Legacy" }
            }
        };
        history.CurrentSessionMessages.Add(new ConversationMessage { Role = "user", Content = "Current" });

        history.MigrateIfNeeded();

        Assert.Single(history.CurrentSessionMessages);
        Assert.Equal("Current", history.CurrentSessionMessages[0].Content);
    }

    [Fact]
    public void MigrateIfNeeded_ClearsEmptyLegacyMessages()
    {
        var history = new ConversationHistory
        {
            Messages = new List<ConversationMessage>()
        };

        history.MigrateIfNeeded();

        Assert.Null(history.Messages);
        Assert.Empty(history.CurrentSessionMessages);
    }

    [Fact]
    public void MigrateIfNeeded_NoOpWhenMessagesNull()
    {
        var history = new ConversationHistory();

        history.MigrateIfNeeded();

        Assert.Null(history.Messages);
        Assert.Empty(history.CurrentSessionMessages);
    }
}
