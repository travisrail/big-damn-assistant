using BigDamnAssistant.Core.Models;

namespace BigDamnAssistant.Core.Services;

public interface IWhatsAppService
{
    Task SendMessageAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default);
    Task SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default);
    Task SendOnChannelAsync(string toPhoneNumber, MessageChannel channel, string message, CancellationToken cancellationToken = default);
    bool ValidateRequest(string signature, string url, IDictionary<string, string> parameters);
    Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken = default);
}
