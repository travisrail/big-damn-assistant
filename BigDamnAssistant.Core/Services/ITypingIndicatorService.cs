using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface ITypingIndicatorService
{
    Task SendAsync(
        string messageSid,
        MessageChannel channel,
        CancellationToken cancellationToken = default);
}
