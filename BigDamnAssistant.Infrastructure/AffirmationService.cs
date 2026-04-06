using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Infrastructure;

public class AffirmationService : IAffirmationService
{
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    private readonly IAffirmationRepository _affirmationRepository;
    private readonly IFamilyMemberRepository _familyMemberRepository;
    private readonly IFamilyMemoryRepository _familyMemoryRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AffirmationService> _logger;

    public AffirmationService(
        IAffirmationRepository affirmationRepository,
        IFamilyMemberRepository familyMemberRepository,
        IFamilyMemoryRepository familyMemoryRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<AffirmationService> logger)
    {
        _affirmationRepository = affirmationRepository;
        _familyMemberRepository = familyMemberRepository;
        _familyMemoryRepository = familyMemoryRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetGeneralAffirmationAsync(CancellationToken cancellationToken = default)
    {
        var affirmations = await _affirmationRepository.GetActiveAffirmationsAsync(cancellationToken);
        var today = GetCentralTimeToday();

        var available = affirmations
            .Where(a => a.LastUsedDate != today)
            .OrderBy(a => a.UsedCount)
            .ThenBy(a => a.LastUsedDate ?? "")
            .ToList();

        if (available.Count > 0)
        {
            var pick = available[0];
            pick.UsedCount++;
            pick.LastUsedDate = today;
            await _affirmationRepository.UpsertAffirmationAsync(pick, cancellationToken);
            return pick.Text;
        }

        // Pool empty or all used today — generate via Claude
        _logger.LogInformation("Affirmation pool empty or exhausted for today, generating via Claude");
        return await GenerateAffirmationAsync(
            """
            Generate a single warm, uplifting daily affirmation suitable for a family.
            It should be positive, genuine, and not overly religious or political.
            2 sentences maximum. Do not start with "I am" — vary the format.
            Respond with only the affirmation text, no preamble.
            """,
            cancellationToken);
    }

    public async Task<string> GetPersonalizedAffirmationAsync(FamilyMember member, CancellationToken cancellationToken = default)
    {
        var memories = await _familyMemoryRepository.GetAllAsync(cancellationToken);
        var relevant = memories
            .Where(m => m.Value.Contains(member.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var prompt = $"Generate a warm, personal daily affirmation for {member.Name}.\n";

        if (relevant.Count > 0)
        {
            prompt += $"What we know about {member.Name}:\n";
            foreach (var memory in relevant)
            {
                prompt += $"- {memory.Value}\n";
            }
        }

        prompt += """
            Make it feel genuinely personal and encouraging. 2-4 sentences. Do not start with "I am".
            Respond with only the affirmation text, no preamble.
            """;

        return await GenerateAffirmationAsync(prompt, cancellationToken);
    }

    public async Task<FamilyMember?> AdvanceRotationAsync(CancellationToken cancellationToken = default)
    {
        var rotation = await _affirmationRepository.GetRotationStateAsync(cancellationToken) ?? new AffirmationRotation();
        var today = GetCentralTimeToday();

        var members = await _familyMemberRepository.GetAllAsync(cancellationToken);
        if (members.Count == 0)
            return null;

        // If already advanced today, return current featured member without advancing
        if (rotation.LastFeaturedDate == today)
        {
            return members.FirstOrDefault(m => m.Id == rotation.LastFeaturedMemberId);
        }

        // Rebuild rotation order if empty or members changed
        var currentMemberIds = members.Select(m => m.Id).OrderBy(id => id).ToList();
        if (rotation.RotationOrder.Count == 0 || !rotation.RotationOrder.OrderBy(id => id).SequenceEqual(currentMemberIds))
        {
            rotation.RotationOrder = currentMemberIds;
        }

        // Find next member in rotation
        var currentIndex = rotation.RotationOrder.IndexOf(rotation.LastFeaturedMemberId);
        var nextIndex = (currentIndex + 1) % rotation.RotationOrder.Count;
        var nextMemberId = rotation.RotationOrder[nextIndex];

        rotation.LastFeaturedMemberId = nextMemberId;
        rotation.LastFeaturedDate = today;
        await _affirmationRepository.UpsertRotationStateAsync(rotation, cancellationToken);

        return members.FirstOrDefault(m => m.Id == nextMemberId);
    }

    private static string GetCentralTimeToday()
    {
        var centralTz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var centralNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, centralTz);
        return centralNow.ToString("yyyy-MM-dd");
    }

    private async Task<string> GenerateAffirmationAsync(string prompt, CancellationToken cancellationToken)
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

        var result = await response.Content.ReadFromJsonAsync<AffirmationContentResponse>(cancellationToken: cancellationToken);
        var text = result?.Content?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("Claude returned empty content for affirmation generation");
            throw new InvalidOperationException("Failed to generate affirmation");
        }

        return text.Trim();
    }
}

file class AffirmationContentResponse
{
    [JsonPropertyName("content")] public List<AffirmationContentBlock>? Content { get; set; }
}

file class AffirmationContentBlock
{
    [JsonPropertyName("text")] public string? Text { get; set; }
}
