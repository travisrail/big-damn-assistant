namespace BigDamnAssistant.Core.Models;

public class FamilyProfile
{
    public string Id { get; set; } = $"profile-{Guid.NewGuid()}";
    public string PartitionKey { get; set; } = "familyProfiles";
    public string Type { get; set; } = "familyProfile";
    public string Name { get; set; } = string.Empty;
    public List<string> Nicknames { get; set; } = new();
    public string? DateOfBirth { get; set; }
    public int? Age { get; set; }
    public string ProfileType { get; set; } = "child";
    public string School { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public List<string> Allergies { get; set; } = new();
    public string MedicalNotes { get; set; } = string.Empty;
    public List<string> Likes { get; set; } = new();
    public List<string> Dislikes { get; set; } = new();
    public List<FamilyActivity> Activities { get; set; } = new();
    public string DoctorName { get; set; } = string.Empty;
    public string EmergencyContact { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; } = true;
}
