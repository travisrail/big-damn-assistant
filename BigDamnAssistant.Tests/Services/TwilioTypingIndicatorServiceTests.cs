using System.Net;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BigDamnAssistant.Tests.Services;

public class TwilioTypingIndicatorServiceTests
{
    private const string AccountSid = "AC_test_sid";
    private const string AuthToken = "test_auth_token";
    private readonly ILogger<TwilioTypingIndicatorService> _logger = Substitute.For<ILogger<TwilioTypingIndicatorService>>();

    private TwilioTypingIndicatorService CreateService(HttpMessageHandler? handler = null)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var httpClient = handler != null ? new HttpClient(handler) : new HttpClient(new FakeHandler());
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new TwilioTypingIndicatorService(AccountSid, AuthToken, httpClientFactory, _logger);
    }

    [Fact]
    public async Task SendAsync_SmsChannel_ReturnsImmediatelyWithoutHttpCall()
    {
        var handler = new FakeHandler();
        var service = CreateService(handler);

        await service.SendAsync("SM123", MessageChannel.SMS, CancellationToken.None);

        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task SendAsync_WhatsAppChannel_MakesPostToCorrectEndpoint()
    {
        var handler = new FakeHandler();
        var service = CreateService(handler);

        await service.SendAsync("SM123", MessageChannel.WhatsApp, CancellationToken.None);

        Assert.True(handler.WasCalled);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            $"https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages/SM123/TypingIndicators",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendAsync_WhatsAppChannel_UsesBasicAuth()
    {
        var handler = new FakeHandler();
        var service = CreateService(handler);

        await service.SendAsync("SM123", MessageChannel.WhatsApp, CancellationToken.None);

        var authHeader = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Basic", authHeader!.Scheme);
        var decoded = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(authHeader.Parameter!));
        Assert.Equal($"{AccountSid}:{AuthToken}", decoded);
    }

    [Fact]
    public async Task SendAsync_EmptyMessageSid_ReturnsWithoutHttpCall()
    {
        var handler = new FakeHandler();
        var service = CreateService(handler);

        await service.SendAsync("", MessageChannel.WhatsApp, CancellationToken.None);

        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task SendAsync_HttpError_DoesNotThrow()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError);
        var service = CreateService(handler);

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync("SM123", MessageChannel.WhatsApp, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_DoesNotThrow()
    {
        var handler = new ThrowingHandler();
        var service = CreateService(handler);

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync("SM123", MessageChannel.WhatsApp, CancellationToken.None));

        Assert.Null(exception);
    }

    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public bool WasCalled { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHandler(HttpStatusCode statusCode = HttpStatusCode.Created)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Network error");
        }
    }
}
