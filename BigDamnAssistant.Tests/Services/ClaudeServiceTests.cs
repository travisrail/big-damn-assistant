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
        var funContentService = Substitute.For<IFunContentService>();
        var affirmationRepo = Substitute.For<IAffirmationRepository>();
        var featureRequestRepo = Substitute.For<IFeatureRequestRepository>();
        var logger = Substitute.For<ILogger<ClaudeService>>();
        return new ClaudeService(httpFactory, calendarService, mailService, whatsAppService, familyMemberRepo, familyMemoryRepo, emailMonitoringRepo, funContentService, affirmationRepo, featureRequestRepo, logger, options);
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
    public void BuildMessages_AppendsUserMessage()
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
