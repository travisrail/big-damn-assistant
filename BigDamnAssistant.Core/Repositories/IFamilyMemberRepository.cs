using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IFamilyMemberRepository
{
    Task<FamilyMember?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FamilyMember>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(FamilyMember member, CancellationToken cancellationToken = default);
}
