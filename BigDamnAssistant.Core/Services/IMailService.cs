namespace BigDamnAssistant.Core.Services;

public class InboundEmail
{
    public string MessageId { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}

public interface IMailService
{
    Task<InboundEmail?> GetMessageAsync(string messageId, CancellationToken cancellationToken = default);
    Task SendMailAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    Task ReplyAsync(string messageId, string body, CancellationToken cancellationToken = default);
}
