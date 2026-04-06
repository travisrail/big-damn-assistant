using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BigDamnAssistant.Tests.Services;

public class AffirmationServiceTests
{
    private readonly IAffirmationRepository _affirmationRepo;
    private readonly IFamilyMemberRepository _familyMemberRepo;
    private readonly IFamilyMemoryRepository _familyMemoryRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AffirmationService> _logger;
    private readonly AffirmationService _service;

    public AffirmationServiceTests()
    {
        _affirmationRepo = Substitute.For<IAffirmationRepository>();
        _familyMemberRepo = Substitute.For<IFamilyMemberRepository>();
        _familyMemoryRepo = Substitute.For<IFamilyMemoryRepository>();
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _logger = Substitute.For<ILogger<AffirmationService>>();
        _service = new AffirmationService(_affirmationRepo, _familyMemberRepo, _familyMemoryRepo, _httpClientFactory, _logger);
    }

    [Fact]
    public async Task AdvanceRotation_FirstRun_BuildsRotationOrder()
    {
        _affirmationRepo.GetRotationStateAsync(Arg.Any<CancellationToken>())
            .Returns((AffirmationRotation?)null);

        var members = new List<FamilyMember>
        {
            new() { Id = "member-a", Name = "Alice" },
            new() { Id = "member-b", Name = "Bob" },
            new() { Id = "member-c", Name = "Charlie" }
        };
        _familyMemberRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(members.AsReadOnly());

        var result = await _service.AdvanceRotationAsync();

        Assert.NotNull(result);
        await _affirmationRepo.Received(1).UpsertRotationStateAsync(
            Arg.Is<AffirmationRotation>(r =>
                r.RotationOrder.Count == 3 &&
                !string.IsNullOrEmpty(r.LastFeaturedMemberId) &&
                !string.IsNullOrEmpty(r.LastFeaturedDate)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceRotation_SameDay_DoesNotAdvance()
    {
        var centralTz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, centralTz).ToString("yyyy-MM-dd");

        var rotation = new AffirmationRotation
        {
            LastFeaturedMemberId = "member-a",
            LastFeaturedDate = today,
            RotationOrder = new List<string> { "member-a", "member-b" }
        };
        _affirmationRepo.GetRotationStateAsync(Arg.Any<CancellationToken>())
            .Returns(rotation);

        var members = new List<FamilyMember>
        {
            new() { Id = "member-a", Name = "Alice" },
            new() { Id = "member-b", Name = "Bob" }
        };
        _familyMemberRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(members.AsReadOnly());

        var result = await _service.AdvanceRotationAsync();

        Assert.NotNull(result);
        Assert.Equal("member-a", result!.Id);
        // Should NOT have saved a new rotation state
        await _affirmationRepo.DidNotReceive().UpsertRotationStateAsync(
            Arg.Any<AffirmationRotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceRotation_NextDay_AdvancesToNextMember()
    {
        var rotation = new AffirmationRotation
        {
            LastFeaturedMemberId = "member-a",
            LastFeaturedDate = "2020-01-01", // Old date, definitely not today
            RotationOrder = new List<string> { "member-a", "member-b", "member-c" }
        };
        _affirmationRepo.GetRotationStateAsync(Arg.Any<CancellationToken>())
            .Returns(rotation);

        var members = new List<FamilyMember>
        {
            new() { Id = "member-a", Name = "Alice" },
            new() { Id = "member-b", Name = "Bob" },
            new() { Id = "member-c", Name = "Charlie" }
        };
        _familyMemberRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(members.AsReadOnly());

        var result = await _service.AdvanceRotationAsync();

        Assert.NotNull(result);
        Assert.Equal("member-b", result!.Id);
        await _affirmationRepo.Received(1).UpsertRotationStateAsync(
            Arg.Is<AffirmationRotation>(r => r.LastFeaturedMemberId == "member-b"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetGeneralAffirmation_PoolHasItems_ReturnsLeastRecentlyUsed()
    {
        var affirmations = new List<AffirmationPoolItem>
        {
            new() { Text = "Used today", UsedCount = 1, LastUsedDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")).ToString("yyyy-MM-dd") },
            new() { Text = "Used yesterday", UsedCount = 1, LastUsedDate = "2020-01-01" },
            new() { Text = "Never used", UsedCount = 0, LastUsedDate = null }
        };
        _affirmationRepo.GetActiveAffirmationsAsync(Arg.Any<CancellationToken>())
            .Returns(affirmations.AsReadOnly());

        var result = await _service.GetGeneralAffirmationAsync();

        Assert.Equal("Never used", result);
        await _affirmationRepo.Received(1).UpsertAffirmationAsync(
            Arg.Is<AffirmationPoolItem>(a => a.Text == "Never used" && a.UsedCount == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetGeneralAffirmation_EmptyPool_FallsBackToClaude()
    {
        _affirmationRepo.GetActiveAffirmationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<AffirmationPoolItem>().AsReadOnly());

        // The service will try to create an HTTP client named "Claude"
        // We verify it attempts the fallback by checking the client factory was called
        _httpClientFactory.CreateClient("Claude").Returns(new HttpClient());

        // This will throw because the HttpClient has no handler configured,
        // but we've verified the fallback path is triggered
        await Assert.ThrowsAnyAsync<Exception>(() => _service.GetGeneralAffirmationAsync());

        _httpClientFactory.Received(1).CreateClient("Claude");
    }
}
