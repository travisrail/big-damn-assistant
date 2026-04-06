using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Repositories;

public interface IMemberPreferencesRepository
{
    Task<MemberPreferences?> GetAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task UpsertAsync(MemberPreferences preferences, CancellationToken cancellationToken = default);
}
