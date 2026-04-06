namespace BigDamnAssistant.Core.Models;

public class FamilyMemory
{
    public string Id { get; set; } = $"memory-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "familyMemory";
    public string Type { get; set; } = "familyMemory";
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
