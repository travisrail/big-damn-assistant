namespace BigDamnAssistant.Core.Models;

public class AffirmationPoolItem
{
    public string Id { get; set; } = $"affirmation-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "affirmations";
    public string Type { get; set; } = "affirmationPool";
    public string Text { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public string AddedByName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public int UsedCount { get; set; } = 0;
    public string? LastUsedDate { get; set; } = null;
    public bool Active { get; set; } = true;
}
