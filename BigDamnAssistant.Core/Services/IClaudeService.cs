using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IClaudeService
{
    Task<string> GetResponseAsync(
        FamilyMember member,
        ConversationHistory history,
        string userMessage,
        CancellationToken cancellationToken = default);

    Task<string> GetKidResponseAsync(
        KidSmsUser kid,
        FamilyProfile profile,
        ConversationHistory history,
        string userMessage,
        CancellationToken cancellationToken = default);
}
