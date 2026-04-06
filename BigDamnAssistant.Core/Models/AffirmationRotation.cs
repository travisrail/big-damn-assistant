namespace BigDamnAssistant.Core.Models;

public class AffirmationRotation
{
    public string Id { get; set; } = "affirmation-rotation";
    public string PartitionKey { get; set; } = "system";
    public string Type { get; set; } = "affirmationRotation";
    public string LastFeaturedMemberId { get; set; } = string.Empty;
    public string LastFeaturedDate { get; set; } = string.Empty;
    public List<string> RotationOrder { get; set; } = new();
}
