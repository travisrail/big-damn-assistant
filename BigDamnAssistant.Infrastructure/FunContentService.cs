using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class FunContentService : IFunContentService
{
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FunContentService> _logger;

    public FunContentService(IHttpClientFactory httpClientFactory, ILogger<FunContentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GenerateJokeAsync(CancellationToken cancellationToken = default)
    {
        return await GenerateContentAsync(
            """
            Generate a single clean, family-friendly joke suitable for all ages.
            It should be genuinely funny, not groan-worthy unless it's a good pun.
            Respond with only the joke text, no preamble, no explanation.
            Format as the setup on one line, then the punchline on the next line.
            """,
            cancellationToken);
    }

    public async Task<string> GenerateFactAsync(string? topic = null, CancellationToken cancellationToken = default)
    {
        var topicLine = string.IsNullOrEmpty(topic)
            ? "Pick any topic — science, history, geography, nature, space, animals, food, sports, world records, or anything else fascinating."
            : $"The topic should be about: {topic}.";

        return await GenerateContentAsync(
            $"""
            Generate a single genuinely interesting and surprising fact.
            {topicLine}
            Make it engaging and conversational, 2-3 sentences maximum.
            Vary the opening — do not always start with "Did you know".
            Respond with only the fact text, no preamble.
            """,
            cancellationToken);
    }

    private async Task<string> GenerateContentAsync(string prompt, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Claude");

        var request = new
        {
            model = Model,
            max_tokens = 256,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var response = await client.PostAsJsonAsync(ApiUrl, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ContentResponse>(cancellationToken: cancellationToken);
        var text = result?.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("Claude returned empty content for fun generation");
            throw new InvalidOperationException("Failed to generate content");
        }

        return text.Trim();
    }
}

file class ContentResponse
{
    [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
}

file class ContentBlock
{
    [JsonPropertyName("text")] public string? Text { get; set; }
}
