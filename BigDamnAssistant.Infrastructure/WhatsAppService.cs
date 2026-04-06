using System.Net.Http.Headers;
using System.Text;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace BigDamnAssistant.Infrastructure;

public class WhatsAppService : IWhatsAppService
{
    private readonly string _fromNumber;
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        string fromNumber,
        string accountSid,
        string authToken,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppService> logger)
    {
        _fromNumber = fromNumber;
        _accountSid = accountSid;
        _authToken = authToken;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendMessageAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await MessageResource.CreateAsync(
                to: new PhoneNumber($"whatsapp:{toPhoneNumber}"),
                from: new PhoneNumber($"whatsapp:{_fromNumber}"),
                body: message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message");
            throw;
        }
    }

    public async Task SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await MessageResource.CreateAsync(
                to: new PhoneNumber(toPhoneNumber),
                from: new PhoneNumber(_fromNumber),
                body: message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS message");
            throw;
        }
    }

    public async Task SendOnChannelAsync(string toPhoneNumber, MessageChannel channel, string message, CancellationToken cancellationToken = default)
    {
        if (channel == MessageChannel.SMS)
            await SendSmsAsync(toPhoneNumber, message, cancellationToken);
        else
            await SendMessageAsync(toPhoneNumber, message, cancellationToken);
    }

    public bool ValidateRequest(string signature, string url, IDictionary<string, string> parameters)
    {
        var validator = new Twilio.Security.RequestValidator(_authToken);
        return validator.Validate(url, parameters, signature);
    }

    public async Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        _logger.LogInformation("Downloading Twilio media from {Url}", mediaUrl);
        return await client.GetByteArrayAsync(mediaUrl, cancellationToken);
    }
}
