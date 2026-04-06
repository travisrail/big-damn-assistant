namespace BigDamnAssistant.Core.Models;

public class SessionSummary
{
    public string Summary { get; set; } = string.Empty;
    public DateTime SessionDate { get; set; }
    public int MessageCount { get; set; }
    public DateTime CompressedAt { get; set; } = DateTime.UtcNow;
}
