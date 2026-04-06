using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IPreferenceDetectionService
{
    Task<PreferenceDetectionResult?> DetectPreferenceAsync(
        string userMessage,
        string assistantResponse,
        CancellationToken cancellationToken = default);
}
