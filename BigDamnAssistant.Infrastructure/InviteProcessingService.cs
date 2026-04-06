using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class InviteProcessingService : IInviteProcessingService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-4-20250514";

    private static readonly HashSet<string> SupportedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private const string ExtractionPrompt = """
        You are analyzing an image that may be a birthday party invitation. Extract the following details if present:

        - Child's name (whose birthday it is)
        - Date of the party (include day of week if visible)
        - Time of the party
        - Venue name
        - Venue address
        - RSVP deadline
        - RSVP contact (name, phone, or email)
        - Any other relevant details (dress code, theme, gift registry, etc.)

        Respond only in JSON with no preamble or markdown:
        {
          "isBirthdayInvite": true,
          "childName": "",
          "partyDate": "ISO 8601 date if determinable, otherwise null",
          "partyTime": "HH:mm if determinable, otherwise null",
          "venueName": "",
          "venueAddress": "",
          "rsvpDeadline": "ISO 8601 date if determinable, otherwise null",
          "rsvpContact": "",
          "additionalDetails": "",
          "missingFields": ["list of fields that could not be determined"]
        }

        If this image is NOT a birthday party invitation, set isBirthdayInvite to false and leave all other fields empty.
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InviteProcessingService> _logger;

    public InviteProcessingService(IHttpClientFactory httpClientFactory, ILogger<InviteProcessingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<BirthdayInviteDetails?> ExtractInviteDetailsAsync(
        byte[] imageBytes,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        if (!SupportedMediaTypes.Contains(mediaType))
        {
            _logger.LogWarning("Unsupported media type for invite processing: {MediaType}", mediaType);
            return null;
        }

        var client = _httpClientFactory.CreateClient("Claude");
        var base64Image = Convert.ToBase64String(imageBytes);

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = Model,
            ["max_tokens"] = 1024,
            ["messages"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "image",
                            ["source"] = new Dictionary<string, string>
                            {
                                ["type"] = "base64",
                                ["media_type"] = mediaType,
                                ["data"] = base64Image
                            }
                        },
                        new Dictionary<string, string>
                        {
                            ["type"] = "text",
                            ["text"] = ExtractionPrompt
                        }
                    }
                }
            }
        };

        try
        {
            var response = await client.PostAsJsonAsync(ApiUrl, requestBody, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Claude vision API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                return null;
            }

            var result = JsonSerializer.Deserialize<VisionResponse>(responseBody);
            var text = result?.Content?.FirstOrDefault()?.Text;

            if (string.IsNullOrEmpty(text))
            {
                _logger.LogWarning("Claude vision returned empty response");
                return null;
            }

            _logger.LogInformation("Claude vision response: {Response}", text);

            // Clean up JSON if wrapped in code block
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                text = text.Replace("```json", "").Replace("```", "").Trim();
            }

            var details = JsonSerializer.Deserialize<BirthdayInviteDetails>(text);
            return details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract invite details from image: {Message}", ex.Message);
            return null;
        }
    }
}

file class VisionResponse
{
    [JsonPropertyName("content")]
    public List<VisionContentBlock>? Content { get; set; }
}

file class VisionContentBlock
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
