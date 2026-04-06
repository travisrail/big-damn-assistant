using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IFamilyMemoryRepository
{
    Task<IReadOnlyList<FamilyMemory>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(FamilyMemory memory, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
