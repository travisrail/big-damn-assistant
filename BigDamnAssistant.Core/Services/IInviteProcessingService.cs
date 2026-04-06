using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IInviteProcessingService
{
    Task<BirthdayInviteDetails?> ExtractInviteDetailsAsync(
        byte[] imageBytes,
        string mediaType,
        CancellationToken cancellationToken = default);
}
