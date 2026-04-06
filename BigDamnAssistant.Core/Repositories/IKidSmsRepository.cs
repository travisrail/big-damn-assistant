using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IKidSmsRepository
{
    Task<KidSmsUser?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<KidSmsUser?> GetByProfileNameAsync(string profileName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KidSmsUser>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(KidSmsUser kid, CancellationToken cancellationToken = default);
}
