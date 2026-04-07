namespace BigDamnAssistant.Core.Models;

public class InboundMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public MessageChannel Channel { get; set; }
    public bool IsGroupChat { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaContentType { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
