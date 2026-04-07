using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Orchestration;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BigDamnAssistant.Tests.Orchestration;

public class MessageOrchestratorTests
{
    private readonly IClaudeService _claudeService = Substitute.For<IClaudeService>();
    private readonly IWhatsAppService _whatsAppService = Substitute.For<IWhatsAppService>();
    private readonly IConversationRepository _conversationRepo = Substitute.For<IConversationRepository>();
    private readonly IFamilyMemberRepository _familyMemberRepo = Substitute.For<IFamilyMemberRepository>();
    private readonly IInviteProcessingService _inviteService = Substitute.For<IInviteProcessingService>();
    private readonly ICalendarService _calendarService = Substitute.For<ICalendarService>();
    private readonly IReminderService _reminderService = Substitute.For<IReminderService>();
    private readonly IEmailMonitoringRepository _emailMonitoringRepo = Substitute.For<IEmailMonitoringRepository>();
    private readonly IMemberPreferencesRepository _memberPreferencesRepo = Substitute.For<IMemberPreferencesRepository>();
    private readonly ISessionCompressionService _sessionCompressionService = Substitute.For<ISessionCompressionService>();
    private readonly IPreferenceDetectionService _preferenceDetectionService = Substitute.For<IPreferenceDetectionService>();
    private readonly IFamilyProfileRepository _familyProfileRepo = Substitute.For<IFamilyProfileRepository>();
    private readonly IKidSmsRepository _kidSmsRepo = Substitute.For<IKidSmsRepository>();
    private readonly IProcessedMessageRepository _processedMessageRepo = Substitute.For<IProcessedMessageRepository>();
    private readonly ILogger<MessageOrchestrator> _logger = Substitute.For<ILogger<MessageOrchestrator>>();
    private readonly AssistantOptions _options = new() { Name = "Big Damn Assistant", TriggerKeyword = "BDA" };
    private readonly MessageOrchestrator _sut;

    public MessageOrchestratorTests()
    {
        _sut = new MessageOrchestrator(
            _claudeService, _whatsAppService, _conversationRepo, _familyMemberRepo,
            _inviteService, _calendarService, _reminderService, _emailMonitoringRepo,
            _memberPreferencesRepo, _familyProfileRepo, _kidSmsRepo,
            _sessionCompressionService, _preferenceDetectionService,
            _processedMessageRepo,
            _logger, Options.Create(_options));
    }

    private FamilyMember SetupKnownMember(string phone = "+15551234567", string name = "Travis")
    {
        var member = new FamilyMember { Id = $"member-{phone}", Name = name, PhoneNumber = phone, Timezone = "America/Chicago" };
        _familyMemberRepo.GetByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns(member);
        _conversationRepo.GetOrCreateAsync(phone, Arg.Any<CancellationToken>())
            .Returns(new ConversationHistory
            {
                Id = $"conv-{phone}",
                PartitionKey = $"conv-{phone}",
                PhoneNumber = phone
            });
        _claudeService.GetResponseAsync(member, Arg.Any<ConversationHistory>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Response");
        return member;
    }

    [Fact]
    public async Task HandleInboundWhatsApp_UnknownNumber_DoesNotRespond()
    {
        _familyMemberRepo.GetByPhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((FamilyMember?)null);

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.DidNotReceive()
            .SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_DirectChat_SendsClaudeResponse()
    {
        SetupKnownMember();

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.WhatsApp, "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_DirectChat_PersistsConversation()
    {
        SetupKnownMember();

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _conversationRepo.Received(1)
            .UpsertAsync(Arg.Is<ConversationHistory>(c =>
                c.CurrentSessionMessages.Count == 2 &&
                c.CurrentSessionMessages[0].Role == "user" &&
                c.CurrentSessionMessages[1].Role == "assistant"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_GroupChat_WithKeyword_ProcessesMessage()
    {
        SetupKnownMember();

        await _sut.HandleInboundMessageAsync("+15551234567", "BDA what's for dinner?", MessageChannel.WhatsApp, isGroupChat: true, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.WhatsApp, "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_GroupChat_WithoutKeyword_IsIgnored()
    {
        SetupKnownMember();

        await _sut.HandleInboundMessageAsync("+15551234567", "Hey everyone, what's up?", MessageChannel.WhatsApp, isGroupChat: true, cancellationToken: CancellationToken.None);

        await _claudeService.DidNotReceive()
            .GetResponseAsync(Arg.Any<FamilyMember>(), Arg.Any<ConversationHistory>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_GroupChat_KeywordCaseInsensitive()
    {
        SetupKnownMember();

        await _sut.HandleInboundMessageAsync("+15551234567", "hey bda, check the calendar", MessageChannel.WhatsApp, isGroupChat: true, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.WhatsApp, "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_MediaMessage_CallsInviteProcessing()
    {
        SetupKnownMember();
        _inviteService.ExtractInviteDetailsAsync(Arg.Any<byte[]>(), "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(new BirthdayInviteDetails { IsBirthdayInvite = false });
        _whatsAppService.DownloadMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });

        await _sut.HandleInboundMessageAsync("+15551234567", "", MessageChannel.WhatsApp, isGroupChat: false,
            mediaUrl: "https://api.twilio.com/media/123", mediaContentType: "image/jpeg",
            cancellationToken: CancellationToken.None);

        await _inviteService.Received(1)
            .ExtractInviteDetailsAsync(Arg.Any<byte[]>(), "image/jpeg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_BirthdayInvite_StoresPendingAction()
    {
        SetupKnownMember();
        _whatsAppService.DownloadMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });
        _inviteService.ExtractInviteDetailsAsync(Arg.Any<byte[]>(), "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(new BirthdayInviteDetails
            {
                IsBirthdayInvite = true,
                ChildName = "Jayden",
                PartyDate = "2026-04-19",
                PartyTime = "14:00",
                VenueName = "Altitude Trampoline Park"
            });

        await _sut.HandleInboundMessageAsync("+15551234567", "", MessageChannel.WhatsApp, isGroupChat: false,
            mediaUrl: "https://api.twilio.com/media/123", mediaContentType: "image/jpeg",
            cancellationToken: CancellationToken.None);

        await _conversationRepo.Received()
            .UpsertAsync(Arg.Is<ConversationHistory>(c =>
                c.PendingAction != null &&
                c.PendingAction.ActionType == "BirthdayInviteConfirmation"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_ConfirmYes_CreatesCalendarEvent()
    {
        var member = SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            PendingAction = new PendingAction
            {
                ActionType = "BirthdayInviteConfirmation",
                Payload = System.Text.Json.JsonSerializer.Serialize(new BirthdayInviteDetails
                {
                    IsBirthdayInvite = true,
                    ChildName = "Jayden",
                    PartyDate = "2026-04-19",
                    PartyTime = "14:00",
                    VenueName = "Fun Zone"
                }),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            }
        };
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundMessageAsync("+15551234567", "yes", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _calendarService.Received(1)
            .CreateEventAsync(Arg.Is<CalendarEvent>(e => e.Subject.Contains("Jayden")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_ConfirmNo_ClearsPendingAction()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            PendingAction = new PendingAction
            {
                ActionType = "BirthdayInviteConfirmation",
                Payload = "{}",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            }
        };
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundMessageAsync("+15551234567", "no", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _calendarService.DidNotReceive()
            .CreateEventAsync(Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
        await _whatsAppService.Received(1)
            .SendMessageAsync("+15551234567", Arg.Is<string>(s => s.Contains("discarded")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_ExpiredPendingAction_ClearsAndProcessesNormally()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            PendingAction = new PendingAction
            {
                ActionType = "BirthdayInviteConfirmation",
                Payload = "{}",
                ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired
            }
        };
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        // Should fall through to normal Claude processing
        await _claudeService.Received(1)
            .GetResponseAsync(Arg.Any<FamilyMember>(), Arg.Any<ConversationHistory>(), "Hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_NonImageMedia_RejectsGracefully()
    {
        SetupKnownMember();

        await _sut.HandleInboundMessageAsync("+15551234567", "", MessageChannel.WhatsApp, isGroupChat: false,
            mediaUrl: "https://api.twilio.com/media/123", mediaContentType: "application/pdf",
            cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendMessageAsync("+15551234567", Arg.Is<string>(s => s.Contains("image files")), Arg.Any<CancellationToken>());
        await _inviteService.DidNotReceive()
            .ExtractInviteDetailsAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Session detection tests

    [Fact]
    public async Task HandleInboundWhatsApp_RecentMessage_DoesNotCompressSession()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            LastMessageAt = DateTime.UtcNow.AddMinutes(-30) // 30 minutes ago, within session
        };
        conversation.CurrentSessionMessages.Add(new ConversationMessage { Role = "user", Content = "Hi" });
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _sessionCompressionService.DidNotReceive()
            .CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_OldMessage_CompressesSession()
    {
        var member = SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            LastMessageAt = DateTime.UtcNow.AddHours(-5) // 5 hours ago, beyond boundary
        };
        conversation.CurrentSessionMessages.Add(new ConversationMessage { Role = "user", Content = "Old message" });
        conversation.CurrentSessionMessages.Add(new ConversationMessage { Role = "assistant", Content = "Old response" });
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);
        _sessionCompressionService.CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), "Travis", Arg.Any<CancellationToken>())
            .Returns("Summary of old conversation.");

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _sessionCompressionService.Received(1)
            .CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), "Travis", Arg.Any<CancellationToken>());
        Assert.Single(conversation.SessionSummaries);
        Assert.Equal("Summary of old conversation.", conversation.SessionSummaries[0].Summary);
    }

    [Fact]
    public async Task HandleInboundWhatsApp_SessionCompression_ClearsCurrentMessages()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            LastMessageAt = DateTime.UtcNow.AddHours(-5)
        };
        conversation.CurrentSessionMessages.Add(new ConversationMessage { Role = "user", Content = "Old" });
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);
        _sessionCompressionService.CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Summary");

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        // Old messages cleared, new exchange added (user + assistant = 2)
        Assert.Equal(2, conversation.CurrentSessionMessages.Count);
        Assert.Equal("user", conversation.CurrentSessionMessages[0].Role);
        Assert.Equal("Hello", conversation.CurrentSessionMessages[0].Content);
    }

    [Fact]
    public async Task HandleInboundWhatsApp_SessionSummaries_TrimmedToMax()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            LastMessageAt = DateTime.UtcNow.AddHours(-5)
        };
        // Pre-fill with max summaries
        for (var i = 0; i < 5; i++)
        {
            conversation.SessionSummaries.Add(new SessionSummary
            {
                Summary = $"Summary {i}",
                SessionDate = DateTime.UtcNow.AddDays(-i - 1),
                MessageCount = 5
            });
        }
        conversation.CurrentSessionMessages.Add(new ConversationMessage { Role = "user", Content = "Old" });
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);
        _sessionCompressionService.CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("New summary");

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        Assert.Equal(5, conversation.SessionSummaries.Count);
        Assert.Equal("New summary", conversation.SessionSummaries[0].Summary);
    }

    [Fact]
    public async Task HandleInboundWhatsApp_SessionCompressionFails_ContinuesWithoutClearing()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            LastMessageAt = DateTime.UtcNow.AddHours(-5)
        };
        conversation.CurrentSessionMessages.Add(new ConversationMessage { Role = "user", Content = "Old message" });
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);
        _sessionCompressionService.CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(x => throw new HttpRequestException("API error"));

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        // Old message still there + new exchange
        Assert.Contains(conversation.CurrentSessionMessages, m => m.Content == "Old message");
        Assert.Empty(conversation.SessionSummaries);
        // Still processes the message
        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.WhatsApp, "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_EmptySession_DoesNotTriggerCompression()
    {
        SetupKnownMember();
        var conversation = new ConversationHistory
        {
            Id = "conv-+15551234567",
            PartitionKey = "conv-+15551234567",
            PhoneNumber = "+15551234567",
            LastMessageAt = DateTime.UtcNow.AddHours(-10)
            // CurrentSessionMessages is empty
        };
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _sessionCompressionService.DidNotReceive()
            .CompressSessionAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Channel detection and kid routing tests

    [Fact]
    public async Task HandleInboundMessage_UnknownNumber_SendsUnrecognizedMessage()
    {
        _familyMemberRepo.GetByPhoneNumberAsync("+15559999999", Arg.Any<CancellationToken>()).Returns((FamilyMember?)null);
        _kidSmsRepo.GetByPhoneNumberAsync("+15559999999", Arg.Any<CancellationToken>()).Returns((KidSmsUser?)null);

        await _sut.HandleInboundMessageAsync("+15559999999", "Hi", MessageChannel.SMS, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15559999999", MessageChannel.SMS, Arg.Is<string>(s => s.Contains("don't recognize")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundMessage_KnownKidSms_UsesKidFlow()
    {
        _familyMemberRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns((FamilyMember?)null);
        var kid = new KidSmsUser { Id = "kidsms-+15558887777", SmsPhoneNumber = "+15558887777", LinkedProfileName = "Teddy", DisplayName = "Teddy" };
        _kidSmsRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns(kid);
        var profile = new FamilyProfile { Name = "Teddy", Age = 8 };
        _familyProfileRepo.GetByNameAsync("Teddy", Arg.Any<CancellationToken>()).Returns(profile);
        _conversationRepo.GetOrCreateAsync("+15558887777", Arg.Any<CancellationToken>())
            .Returns(new ConversationHistory { Id = "conv-+15558887777", PartitionKey = "conv-+15558887777", PhoneNumber = "+15558887777" });
        _claudeService.GetKidResponseAsync(kid, profile, Arg.Any<ConversationHistory>(), "What do I have today?", Arg.Any<CancellationToken>())
            .Returns("You have soccer at 4pm!");

        await _sut.HandleInboundMessageAsync("+15558887777", "What do I have today?", MessageChannel.SMS, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _claudeService.Received(1)
            .GetKidResponseAsync(kid, profile, Arg.Any<ConversationHistory>(), "What do I have today?", Arg.Any<CancellationToken>());
        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15558887777", MessageChannel.SMS, "You have soccer at 4pm!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundMessage_KidOnWhatsApp_UsesKidFlow()
    {
        _familyMemberRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns((FamilyMember?)null);
        var kid = new KidSmsUser { Id = "kidsms-+15558887777", WhatsAppPhoneNumber = "+15558887777", LinkedProfileName = "Emma", DisplayName = "Emma" };
        _kidSmsRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns(kid);
        var profile = new FamilyProfile { Name = "Emma", Age = 10 };
        _familyProfileRepo.GetByNameAsync("Emma", Arg.Any<CancellationToken>()).Returns(profile);
        _conversationRepo.GetOrCreateAsync("+15558887777", Arg.Any<CancellationToken>())
            .Returns(new ConversationHistory { Id = "conv-+15558887777", PartitionKey = "conv-+15558887777", PhoneNumber = "+15558887777" });
        _claudeService.GetKidResponseAsync(kid, profile, Arg.Any<ConversationHistory>(), "Hi!", Arg.Any<CancellationToken>())
            .Returns("Hey Emma!");

        await _sut.HandleInboundMessageAsync("+15558887777", "Hi!", MessageChannel.WhatsApp, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15558887777", MessageChannel.WhatsApp, "Hey Emma!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundMessage_KidWithNoProfile_SendsSetupMessage()
    {
        _familyMemberRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns((FamilyMember?)null);
        var kid = new KidSmsUser { Id = "kidsms-+15558887777", SmsPhoneNumber = "+15558887777", LinkedProfileName = "Teddy", DisplayName = "Teddy" };
        _kidSmsRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns(kid);
        _familyProfileRepo.GetByNameAsync("Teddy", Arg.Any<CancellationToken>()).Returns((FamilyProfile?)null);

        await _sut.HandleInboundMessageAsync("+15558887777", "Hello", MessageChannel.SMS, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15558887777", MessageChannel.SMS, Arg.Is<string>(s => s.Contains("profile")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundMessage_AdultOnSms_UsesAdultFlow()
    {
        var member = new FamilyMember { Id = "member-+15551234567", Name = "Travis", PhoneNumber = "+15551234567", Timezone = "America/Chicago" };
        _familyMemberRepo.GetByPhoneNumberAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(member);
        _conversationRepo.GetOrCreateAsync("+15551234567", Arg.Any<CancellationToken>())
            .Returns(new ConversationHistory { Id = "conv-+15551234567", PartitionKey = "conv-+15551234567", PhoneNumber = "+15551234567" });
        _claudeService.GetResponseAsync(member, Arg.Any<ConversationHistory>(), "Hello", Arg.Any<CancellationToken>())
            .Returns("Hi Travis!");

        await _sut.HandleInboundMessageAsync("+15551234567", "Hello", MessageChannel.SMS, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _claudeService.Received(1)
            .GetResponseAsync(member, Arg.Any<ConversationHistory>(), "Hello", Arg.Any<CancellationToken>());
        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.SMS, "Hi Travis!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundMessage_KidSavesConversation()
    {
        _familyMemberRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns((FamilyMember?)null);
        var kid = new KidSmsUser { Id = "kidsms-+15558887777", SmsPhoneNumber = "+15558887777", LinkedProfileName = "Teddy", DisplayName = "Teddy" };
        _kidSmsRepo.GetByPhoneNumberAsync("+15558887777", Arg.Any<CancellationToken>()).Returns(kid);
        _familyProfileRepo.GetByNameAsync("Teddy", Arg.Any<CancellationToken>()).Returns(new FamilyProfile { Name = "Teddy", Age = 8 });
        _conversationRepo.GetOrCreateAsync("+15558887777", Arg.Any<CancellationToken>())
            .Returns(new ConversationHistory { Id = "conv-+15558887777", PartitionKey = "conv-+15558887777", PhoneNumber = "+15558887777" });
        _claudeService.GetKidResponseAsync(kid, Arg.Any<FamilyProfile>(), Arg.Any<ConversationHistory>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Response!");

        await _sut.HandleInboundMessageAsync("+15558887777", "Hello", MessageChannel.SMS, isGroupChat: false, cancellationToken: CancellationToken.None);

        await _conversationRepo.Received(1)
            .UpsertAsync(Arg.Is<ConversationHistory>(c =>
                c.CurrentSessionMessages.Count == 2 &&
                c.CurrentSessionMessages[0].Content == "Hello" &&
                c.CurrentSessionMessages[1].Content == "Response!"),
                Arg.Any<CancellationToken>());
    }

    // ProcessAsync deduplication tests

    [Fact]
    public async Task ProcessAsync_DuplicateMessageSid_SkipsProcessing()
    {
        SetupKnownMember();
        _processedMessageRepo.ExistsAsync("SM123", Arg.Any<CancellationToken>()).Returns(true);

        var message = new InboundMessage
        {
            MessageSid = "SM123",
            From = "+15551234567",
            Body = "Hello",
            Channel = MessageChannel.WhatsApp
        };

        await _sut.ProcessAsync(message, CancellationToken.None);

        await _claudeService.DidNotReceive()
            .GetResponseAsync(Arg.Any<FamilyMember>(), Arg.Any<ConversationHistory>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NewMessageSid_CreatesDeduplicationDocument()
    {
        SetupKnownMember();
        _processedMessageRepo.ExistsAsync("SM456", Arg.Any<CancellationToken>()).Returns(false);

        var message = new InboundMessage
        {
            MessageSid = "SM456",
            From = "+15551234567",
            Body = "Hello",
            Channel = MessageChannel.WhatsApp
        };

        await _sut.ProcessAsync(message, CancellationToken.None);

        await _processedMessageRepo.Received(1)
            .CreateAsync(Arg.Is<ProcessedMessage>(p => p.MessageSid == "SM456"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NewMessage_ProcessesNormally()
    {
        SetupKnownMember();
        _processedMessageRepo.ExistsAsync("SM789", Arg.Any<CancellationToken>()).Returns(false);

        var message = new InboundMessage
        {
            MessageSid = "SM789",
            From = "+15551234567",
            Body = "Hello",
            Channel = MessageChannel.WhatsApp
        };

        await _sut.ProcessAsync(message, CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.WhatsApp, "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EmptyMessageSid_SkipsDeduplication()
    {
        SetupKnownMember();

        var message = new InboundMessage
        {
            MessageSid = "",
            From = "+15551234567",
            Body = "Hello",
            Channel = MessageChannel.WhatsApp
        };

        await _sut.ProcessAsync(message, CancellationToken.None);

        await _processedMessageRepo.DidNotReceive()
            .ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _whatsAppService.Received(1)
            .SendOnChannelAsync("+15551234567", MessageChannel.WhatsApp, "Response", Arg.Any<CancellationToken>());
    }
}
