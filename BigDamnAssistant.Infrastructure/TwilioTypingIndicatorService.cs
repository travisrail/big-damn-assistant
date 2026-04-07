using System.Net.Http.Headers;
using System.Text;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class TwilioTypingIndicatorService : ITypingIndicatorService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TwilioTypingIndicatorService> _logger;

    public TwilioTypingIndicatorService(
        string accountSid,
        string authToken,
        IHttpClientFactory httpClientFactory,
        ILogger<TwilioTypingIndicatorService> logger)
    {
        _accountSid = accountSid;
        _authToken = authToken;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAsync(
        string messageSid,
        MessageChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (channel != MessageChannel.WhatsApp)
            return;

        if (string.IsNullOrEmpty(messageSid))
            return;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var url = $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/Messages/{messageSid}/TypingIndicators";
            var response = await client.PostAsync(url, null, cancellationToken);

            _logger.LogDebug("Typing indicator sent for {MessageSid}: {StatusCode}", messageSid, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Typing indicator failed for {MessageSid}", messageSid);
        }
    }
}
