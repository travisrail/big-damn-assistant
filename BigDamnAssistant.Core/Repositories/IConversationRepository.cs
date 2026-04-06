using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IConversationRepository
{
    Task<ConversationHistory?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task UpsertAsync(ConversationHistory conversation, CancellationToken cancellationToken = default);
}
