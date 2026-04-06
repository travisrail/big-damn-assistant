using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IAffirmationRepository
{
    Task<IReadOnlyList<AffirmationPoolItem>> GetActiveAffirmationsAsync(CancellationToken cancellationToken = default);
    Task UpsertAffirmationAsync(AffirmationPoolItem affirmation, CancellationToken cancellationToken = default);
    Task<bool> RemoveAffirmationAsync(string text, CancellationToken cancellationToken = default);
    Task<AffirmationRotation?> GetRotationStateAsync(CancellationToken cancellationToken = default);
    Task UpsertRotationStateAsync(AffirmationRotation rotation, CancellationToken cancellationToken = default);
}
