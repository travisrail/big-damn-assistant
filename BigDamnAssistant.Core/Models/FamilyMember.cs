namespace BigDamnAssistant.Core.Models;

public class FamilyMember
{
    public string Id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = "members";
    public string Type { get; set; } = "familyMember";
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Nicknames { get; set; } = new();
    public string Role { get; set; } = "member";
    public string Timezone { get; set; } = "America/Chicago";
    public string Location { get; set; } = string.Empty;
}
