using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface ISessionCompressionService
{
    Task<string> CompressSessionAsync(
        IReadOnlyList<ConversationMessage> messages,
        string memberName,
        CancellationToken cancellationToken = default);
}
