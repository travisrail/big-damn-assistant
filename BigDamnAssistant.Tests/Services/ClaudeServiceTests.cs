using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BigDamnAssistant.Tests.Services;

public class ClaudeServiceTests
{
    private static ClaudeService CreateService(string assistantName = "Big Damn Assistant")
    {
        var options = Options.Create(new AssistantOptions { Name = assistantName });
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var calendarService = Substitute.For<ICalendarService>();
        var mailService = Substitute.For<IMailService>();
        var whatsAppService = Substitute.For<IWhatsAppService>();
        var familyMemberRepo = Substitute.For<IFamilyMemberRepository>();
        var familyMemoryRepo = Substitute.For<IFamilyMemoryRepository>();
        var emailMonitoringRepo = Substitute.For<IEmailMonitoringRepository>();
        var memberPreferencesRepo = Substitute.For<IMemberPreferencesRepository>();
        var funContentService = Substitute.For<IFunContentService>();
        var affirmationRepo = Substitute.For<IAffirmationRepository>();
        var featureRequestRepo = Substitute.For<IFeatureRequestRepository>();
        var logger = Substitute.For<ILogger<ClaudeService>>();
        return new ClaudeService(httpFactory, calendarService, mailService, whatsAppService,
            familyMemberRepo, familyMemoryRepo, emailMonitoringRepo, memberPreferencesRepo,
            funContentService, affirmationRepo, featureRequestRepo, logger, options);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesMemberName()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now);

        Assert.Contains("Travis", prompt);
        Assert.Contains("America/Chicago", prompt);
        Assert.Contains("April 4, 2026", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UsesConfiguredAssistantName()
    {
        var service = CreateService("Jarvis");
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now);

        Assert.Contains("Jarvis", prompt);
        Assert.DoesNotContain("Big Damn Assistant", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UsesDefaultName_WhenNotConfigured()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now);

        Assert.Contains("Big Damn Assistant", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesMemories_WhenPresent()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var memories = new List<FamilyMemory>
        {
            new() { Key = "george shoe size", Value = "George's shoe size is 9", CreatedByName = "Travis" },
            new() { Key = "wifi password", Value = "Wifi password is sunshine123", CreatedByName = "Sarah" }
        };

        var prompt = service.BuildSystemPrompt(member, now, memories);

        Assert.Contains("Family Knowledge Base", prompt);
        Assert.Contains("George's shoe size is 9", prompt);
        Assert.Contains("saved by Travis", prompt);
        Assert.Contains("Wifi password is sunshine123", prompt);
        Assert.Contains("saved by Sarah", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_OmitsKnowledgeBase_WhenNoMemories()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now, new List<FamilyMemory>());

        Assert.DoesNotContain("Family Knowledge Base", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_OmitsKnowledgeBase_WhenNull()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now, null);

        Assert.DoesNotContain("Family Knowledge Base", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesClearConversationInstruction()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now);

        Assert.Contains("clear_conversation", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesPreferences_WhenPresent()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));
        var prefs = new MemberPreferences
        {
            CommunicationStyle = "formal",
            BriefingLength = "short",
            LearnedPreferences = new Dictionary<string, string>
            {
                ["diet"] = "vegetarian",
                ["units"] = "metric"
            }
        };

        var prompt = service.BuildSystemPrompt(member, now, preferences: prefs);

        Assert.Contains("Travis's Preferences", prompt);
        Assert.Contains("formal", prompt);
        Assert.Contains("short", prompt);
        Assert.Contains("vegetarian", prompt);
        Assert.Contains("metric", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_OmitsPreferences_WhenNull()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now, preferences: null);

        Assert.DoesNotContain("Preferences", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesSessionSummaries_WhenPresent()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));
        var summaries = new List<SessionSummary>
        {
            new() { Summary = "Discussed weekend plans.", SessionDate = new DateTime(2026, 4, 3), MessageCount = 8 },
            new() { Summary = "Checked calendar events.", SessionDate = new DateTime(2026, 4, 2), MessageCount = 4 }
        };

        var prompt = service.BuildSystemPrompt(member, now, sessionSummaries: summaries);

        Assert.Contains("Conversation History Summary", prompt);
        Assert.Contains("Discussed weekend plans.", prompt);
        Assert.Contains("Checked calendar events.", prompt);
        Assert.Contains("8 messages", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_OmitsSessionSummaries_WhenEmpty()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now, sessionSummaries: new List<SessionSummary>());

        Assert.DoesNotContain("Conversation History Summary", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesPreferenceToolInstruction()
    {
        var service = CreateService();
        var member = new FamilyMember { Name = "Travis", Timezone = "America/Chicago" };
        var now = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-5));

        var prompt = service.BuildSystemPrompt(member, now);

        Assert.Contains("set_preference", prompt);
        Assert.Contains("list_preferences", prompt);
        Assert.Contains("remove_preference", prompt);
    }

    [Fact]
    public void BuildMessages_UsesCurrentSessionMessages()
    {
        var history = new ConversationHistory();
        history.AddMessage("user", "Hi");
        history.AddMessage("assistant", "Hello!");

        var messages = ClaudeService.BuildMessages(history, "What's up?");

        Assert.Equal(3, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("user", messages[2].Role);
    }

    [Fact]
    public void BuildMessages_EmptyHistory_ReturnsOnlyUserMessage()
    {
        var history = new ConversationHistory();

        var messages = ClaudeService.BuildMessages(history, "Hello");

        Assert.Single(messages);
        Assert.Equal("user", messages[0].Role);
    }
}
