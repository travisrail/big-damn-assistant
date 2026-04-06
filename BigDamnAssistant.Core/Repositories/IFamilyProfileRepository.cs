using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IFamilyProfileRepository
{
    Task<IReadOnlyList<FamilyProfile>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<FamilyProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task UpsertAsync(FamilyProfile profile, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(string name, CancellationToken cancellationToken = default);
}
