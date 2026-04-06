using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class SessionCompressionService : ISessionCompressionService
{
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SessionCompressionService> _logger;

    public SessionCompressionService(IHttpClientFactory httpClientFactory, ILogger<SessionCompressionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> CompressSessionAsync(
        IReadOnlyList<ConversationMessage> messages,
        string memberName,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("Claude");

        var formattedMessages = string.Join("\n", messages.Select(m =>
            m.Role == "user" ? $"{memberName}: {m.Content}" : $"BDA: {m.Content}"));

        var request = new
        {
            model = Model,
            max_tokens = 256,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = $"""
                        Summarize the following conversation into 3-5 sentences.
                        Capture only meaningful information: decisions made, preferences expressed,
                        tasks completed, important facts mentioned, and follow-up items.
                        Discard pleasantries, confirmations, and filler responses like "ok", "thanks", "got it".
                        Be concise — this summary will be used as context for future conversations.

                        Conversation:
                        {formattedMessages}

                        Respond with only the summary text, no preamble.
                        """
                }
            }
        };

        var response = await client.PostAsJsonAsync(ApiUrl, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompressResponse>(cancellationToken: cancellationToken);
        var text = result?.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException("Session compression returned empty response");

        _logger.LogInformation("Compressed {MessageCount} messages into session summary", messages.Count);
        return text;
    }

    private class CompressResponse
    {
        [JsonPropertyName("content")]
        public List<CompressContent>? Content { get; set; }
    }

    private class CompressContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
