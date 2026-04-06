using System.Text.Json.Serialization;

namespace BigDamnAssistant.Core.Models;

public class BirthdayInviteDetails
{
    [JsonPropertyName("isBirthdayInvite")]
    public bool IsBirthdayInvite { get; set; }

    [JsonPropertyName("childName")]
    public string ChildName { get; set; } = string.Empty;

    [JsonPropertyName("partyDate")]
    public string? PartyDate { get; set; }

    [JsonPropertyName("partyTime")]
    public string? PartyTime { get; set; }

    [JsonPropertyName("venueName")]
    public string VenueName { get; set; } = string.Empty;

    [JsonPropertyName("venueAddress")]
    public string VenueAddress { get; set; } = string.Empty;

    [JsonPropertyName("rsvpDeadline")]
    public string? RsvpDeadline { get; set; }

    [JsonPropertyName("rsvpContact")]
    public string RsvpContact { get; set; } = string.Empty;

    [JsonPropertyName("additionalDetails")]
    public string AdditionalDetails { get; set; } = string.Empty;

    [JsonPropertyName("missingFields")]
    public List<string> MissingFields { get; set; } = new();
}
