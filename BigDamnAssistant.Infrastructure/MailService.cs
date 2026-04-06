using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.Messages.Item.Reply;

namespace BigDamnAssistant.Infrastructure;

public class MailService : IMailService
{
    private readonly GraphServiceClient _graphClient;
    private readonly string _userId;
    private readonly ILogger<MailService> _logger;

    public MailService(GraphServiceClient graphClient, string userId, ILogger<MailService> logger)
    {
        _graphClient = graphClient;
        _userId = userId;
        _logger = logger;
    }

    public async Task<InboundEmail?> GetMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var msg = await _graphClient.Users[_userId].Messages[messageId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[] { "id", "from", "subject", "bodyPreview", "receivedDateTime" };
                }, cancellationToken);

            if (msg is null) return null;

            return new InboundEmail
            {
                MessageId = msg.Id ?? string.Empty,
                From = msg.From?.EmailAddress?.Address ?? string.Empty,
                Subject = msg.Subject ?? string.Empty,
                Body = msg.BodyPreview ?? string.Empty,
                ReceivedAt = msg.ReceivedDateTime ?? DateTimeOffset.MinValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message {MessageId}", messageId);
            throw;
        }
    }

    public async Task SendMailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody { Content = body, ContentType = BodyType.Text },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = to } }
                }
            };

            await _graphClient.Users[_userId].SendMail.PostAsync(
                new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send mail to {To}", to);
            throw;
        }
    }

    public async Task ReplyAsync(string messageId, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            await _graphClient.Users[_userId].Messages[messageId].Reply.PostAsync(
                new ReplyPostRequestBody
                {
                    Comment = body
                }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reply to message {MessageId}", messageId);
            throw;
        }
    }
}
