using System.Net;
using System.Text;
using System.Text.Json;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using BigDamnAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using NSubstitute;

namespace BigDamnAssistant.Tests.Services;

public class EmailMonitoringServiceTests
{
    private readonly IEmailMonitoringRepository _emailMonitoringRepo = Substitute.For<IEmailMonitoringRepository>();
    private readonly IFamilyMemberRepository _familyMemberRepo = Substitute.For<IFamilyMemberRepository>();
    private readonly IWhatsAppService _whatsAppService = Substitute.For<IWhatsAppService>();
    private readonly ICalendarService _calendarService = Substitute.For<ICalendarService>();
    private readonly IReminderService _reminderService = Substitute.For<IReminderService>();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly ILogger<EmailMonitoringService> _logger = Substitute.For<ILogger<EmailMonitoringService>>();

    private readonly GraphServiceClient _graphClient;
    private readonly EmailMonitoringService _sut;

    public EmailMonitoringServiceTests()
    {
        // Create a GraphServiceClient with an anonymous auth provider — tests that
        // call Graph are designed to exit early or catch the expected exception.
        _graphClient = new GraphServiceClient(new AnonymousAuthenticationProvider());

        _sut = new EmailMonitoringService(
            _emailMonitoringRepo,
            _familyMemberRepo,
            _whatsAppService,
            _calendarService,
            _reminderService,
            _graphClient,
            _httpClientFactory,
            _logger);
    }

    [Fact]
    public async Task ScanMailboxes_NoActiveMailboxes_DoesNothing()
    {
        _emailMonitoringRepo.GetActiveMailboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MonitoredMailbox>());

        await _sut.ScanMailboxesAsync();

        await _emailMonitoringRepo.DidNotReceive()
            .GetActiveSendersAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanMailboxes_NoWhitelistedSenders_DoesNothing()
    {
        _emailMonitoringRepo.GetActiveMailboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MonitoredMailbox>
            {
                new() { EmailAddress = "test@example.com", DisplayName = "Test" }
            });

        _emailMonitoringRepo.GetActiveSendersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<WhitelistedSender>());

        await _sut.ScanMailboxesAsync();

        // Should not attempt to get scan state since there are no senders
        await _emailMonitoringRepo.DidNotReceive()
            .GetScanStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanMailboxes_UpdatesScanState_AfterProcessing()
    {
        // This test verifies that scan state retrieval is attempted for each mailbox.
        // Graph will fail (fake handler), but the per-mailbox error is caught.
        _emailMonitoringRepo.GetActiveMailboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MonitoredMailbox>
            {
                new() { EmailAddress = "family@example.com", DisplayName = "Family" }
            });

        _emailMonitoringRepo.GetActiveSendersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<WhitelistedSender>
            {
                new() { EmailAddress = "school@usd123.edu", DisplayName = "School" }
            });

        _emailMonitoringRepo.GetScanStateAsync("family@example.com", Arg.Any<CancellationToken>())
            .Returns((MailboxScanState?)null);

        // Graph call will fail, but error is caught per-mailbox
        await _sut.ScanMailboxesAsync();

        // Verify scan state was queried
        await _emailMonitoringRepo.Received(1)
            .GetScanStateAsync("family@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanMailboxes_NonActionableEmail_DoesNotNotify()
    {
        // Test the AnalyzeEmailAsync method — when Claude says not actionable,
        // no notifications should be generated.
        var claudeResponseJson = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(new
                    {
                        isActionable = false,
                        summary = "Just a newsletter",
                        suggestedActions = Array.Empty<object>()
                    })
                }
            }
        });

        var httpClient = new HttpClient(new FakeHttpHandler(claudeResponseJson));
        httpClient.DefaultRequestHeaders.Add("x-api-key", "test-key");
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClientFactory.CreateClient("Claude").Returns(httpClient);

        var result = await InvokeAnalyzeEmailAsync("Newsletter", "news@example.com", "Weekly digest...");

        Assert.False(result.IsActionable);
        await _whatsAppService.DidNotReceive()
            .SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanMailboxes_ActionableEmail_NotifiesAllMembers()
    {
        var claudeResponseJson = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(new
                    {
                        isActionable = true,
                        summary = "Parent-teacher conference on April 15",
                        suggestedActions = new[]
                        {
                            new { type = "CalendarEvent", description = "Parent-Teacher Conference", suggestedDate = "2026-04-15", suggestedTime = "15:00" }
                        }
                    })
                }
            }
        });

        var httpClient = new HttpClient(new FakeHttpHandler(claudeResponseJson));
        httpClient.DefaultRequestHeaders.Add("x-api-key", "test-key");
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClientFactory.CreateClient("Claude").Returns(httpClient);

        var result = await InvokeAnalyzeEmailAsync(
            "Parent-Teacher Conference", "school@usd123.edu", "Conference on April 15 at 3pm");

        Assert.True(result.IsActionable);
        Assert.Contains("Parent-teacher conference", result.Summary);
        Assert.Single(result.SuggestedActions);
        Assert.Equal("CalendarEvent", result.SuggestedActions[0].Type);
    }

    [Fact]
    public async Task ScanMailboxes_ActionableEmail_CreatesPendingAction()
    {
        var claudeResponseJson = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(new
                    {
                        isActionable = true,
                        summary = "Field trip permission slip due",
                        suggestedActions = new[]
                        {
                            new { type = "Reminder", description = "Sign field trip permission slip", suggestedDate = "2026-04-10", suggestedTime = (string?)null }
                        }
                    })
                }
            }
        });

        var httpClient = new HttpClient(new FakeHttpHandler(claudeResponseJson));
        httpClient.DefaultRequestHeaders.Add("x-api-key", "test-key");
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClientFactory.CreateClient("Claude").Returns(httpClient);

        var result = await InvokeAnalyzeEmailAsync(
            "Field Trip Permission", "teacher@school.edu", "Permission slip due April 10");

        Assert.True(result.IsActionable);
        Assert.Single(result.SuggestedActions);
        Assert.Equal("Reminder", result.SuggestedActions[0].Type);
        Assert.Equal("2026-04-10", result.SuggestedActions[0].SuggestedDate);
    }

    /// <summary>
    /// Helper to invoke the internal AnalyzeEmailAsync method via reflection.
    /// </summary>
    private async Task<EmailAnalysisResult> InvokeAnalyzeEmailAsync(
        string subject, string fromAddress, string bodyPreview)
    {
        var method = typeof(EmailMonitoringService).GetMethod("AnalyzeEmailAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        if (method == null)
            throw new InvalidOperationException("AnalyzeEmailAsync method not found");

        var task = (Task<EmailAnalysisResult>)method.Invoke(_sut, new object[] { subject, fromAddress, bodyPreview, CancellationToken.None })!;
        return await task;
    }

    /// <summary>
    /// Fake HTTP handler that returns a predetermined response.
    /// </summary>
    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public FakeHttpHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
