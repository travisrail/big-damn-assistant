using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IAffirmationService
{
    Task<string> GetGeneralAffirmationAsync(CancellationToken cancellationToken = default);
    Task<string> GetPersonalizedAffirmationAsync(FamilyMember member, CancellationToken cancellationToken = default);
    Task<FamilyMember?> AdvanceRotationAsync(CancellationToken cancellationToken = default);
}
