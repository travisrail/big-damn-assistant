using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Tests.Models;

public class ConversationHistoryTests
{
    [Fact]
    public void AddMessage_AppendsToList()
    {
        var history = new ConversationHistory();

        history.AddMessage("user", "Hello");

        Assert.Single(history.Messages);
        Assert.Equal("user", history.Messages[0].Role);
        Assert.Equal("Hello", history.Messages[0].Content);
    }

    [Fact]
    public void AddMessage_TrimsOldMessages_WhenExceedingMax()
    {
        var history = new ConversationHistory();

        for (int i = 0; i < ConversationHistory.MaxMessages + 5; i++)
        {
            history.AddMessage("user", $"Message {i}");
        }

        Assert.Equal(ConversationHistory.MaxMessages, history.Messages.Count);
        Assert.Equal($"Message 5", history.Messages[0].Content);
    }

    [Fact]
    public void AddMessage_UpdatesTimestamp()
    {
        var history = new ConversationHistory();
        var before = DateTimeOffset.UtcNow;

        history.AddMessage("user", "Hello");

        Assert.True(history.UpdatedAt >= before);
    }
}
