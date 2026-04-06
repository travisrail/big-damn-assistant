using System.Text.Json.Serialization;

namespace BigDamnAssistant.Core.Models;

public class PreferenceDetectionResult
{
    [JsonPropertyName("preferenceDetected")]
    public bool PreferenceDetected { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("learnedKey")]
    public string? LearnedKey { get; set; }

    [JsonPropertyName("learnedValue")]
    public string? LearnedValue { get; set; }
}
