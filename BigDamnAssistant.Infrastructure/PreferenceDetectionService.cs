using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class PreferenceDetectionService : IPreferenceDetectionService
{
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PreferenceDetectionService> _logger;

    public PreferenceDetectionService(IHttpClientFactory httpClientFactory, ILogger<PreferenceDetectionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PreferenceDetectionResult?> DetectPreferenceAsync(
        string userMessage,
        string assistantResponse,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("Claude");

        var request = new
        {
            model = Model,
            max_tokens = 128,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = $$"""
                        Did the following exchange express a personal preference, communication style request, or behavioral instruction from the user?

                        Examples of preferences:
                        - "keep my briefings short" → briefingLength = short
                        - "don't message me before 8am" → quietHoursEnd = 08:00
                        - "I'm vegetarian" → learnedPreferences: diet = vegetarian
                        - "I prefer metric" → learnedPreferences: units = metric
                        - "I like detailed responses" → communicationStyle = detailed

                        User message: "{{userMessage}}"
                        Assistant response: "{{assistantResponse}}"

                        Respond only in JSON:
                        {"preferenceDetected": true|false, "field": "field name on MemberPreferences if a structured field applies, otherwise null", "value": "new value if structured field", "learnedKey": "short key if free-form learned preference, otherwise null", "learnedValue": "value if free-form learned preference, otherwise null"}
                        """
                }
            }
        };

        var response = await client.PostAsJsonAsync(ApiUrl, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var apiResult = await response.Content.ReadFromJsonAsync<DetectionResponse>(cancellationToken: cancellationToken);
        var text = apiResult?.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
            return null;

        var result = JsonSerializer.Deserialize<PreferenceDetectionResult>(text);
        return result;
    }

    private class DetectionResponse
    {
        [JsonPropertyName("content")]
        public List<DetectionContent>? Content { get; set; }
    }

    private class DetectionContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
