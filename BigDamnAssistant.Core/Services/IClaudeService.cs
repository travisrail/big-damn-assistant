using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IClaudeService
{
    Task<string> GetResponseAsync(
        FamilyMember member,
        ConversationHistory history,
        string userMessage,
        CancellationToken cancellationToken = default);
}
