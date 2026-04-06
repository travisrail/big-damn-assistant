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
    private readonly ILogger<MessageOrchestrator> _logger = Substitute.For<ILogger<MessageOrchestrator>>();
    private readonly MessageOrchestrator _sut;

    public MessageOrchestratorTests()
    {
        var options = Options.Create(new AssistantOptions { Name = "Big Damn Assistant", TriggerKeyword = "BDA" });
        _sut = new MessageOrchestrator(
            _claudeService, _whatsAppService, _conversationRepo, _familyMemberRepo,
            _inviteService, _calendarService, _reminderService, _emailMonitoringRepo, _logger, options);
    }

    private FamilyMember SetupKnownMember(string phone = "+15551234567", string name = "Travis")
    {
        var member = new FamilyMember { Id = $"member-{phone}", Name = name, PhoneNumber = phone, Timezone = "America/Chicago" };
        _familyMemberRepo.GetByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns(member);
        _conversationRepo.GetByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns((ConversationHistory?)null);
        _claudeService.GetResponseAsync(member, Arg.Any<ConversationHistory>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Response");
        return member;
    }

    [Fact]
    public async Task HandleInboundWhatsApp_UnknownNumber_DoesNotRespond()
    {
        _familyMemberRepo.GetByPhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((FamilyMember?)null);

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "Hello", isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.DidNotReceive()
            .SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_DirectChat_SendsClaudeResponse()
    {
        SetupKnownMember();

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "Hello", isGroupChat: false, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendMessageAsync("+15551234567", "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_DirectChat_PersistsConversation()
    {
        SetupKnownMember();

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "Hello", isGroupChat: false, cancellationToken: CancellationToken.None);

        await _conversationRepo.Received(1)
            .UpsertAsync(Arg.Is<ConversationHistory>(c =>
                c.Messages.Count == 2 &&
                c.Messages[0].Role == "user" &&
                c.Messages[1].Role == "assistant"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_GroupChat_WithKeyword_ProcessesMessage()
    {
        SetupKnownMember();

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "BDA what's for dinner?", isGroupChat: true, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendMessageAsync("+15551234567", "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_GroupChat_WithoutKeyword_IsIgnored()
    {
        SetupKnownMember();

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "Hey everyone, what's up?", isGroupChat: true, cancellationToken: CancellationToken.None);

        await _claudeService.DidNotReceive()
            .GetResponseAsync(Arg.Any<FamilyMember>(), Arg.Any<ConversationHistory>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_GroupChat_KeywordCaseInsensitive()
    {
        SetupKnownMember();

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "hey bda, check the calendar", isGroupChat: true, cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendMessageAsync("+15551234567", "Response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_MediaMessage_CallsInviteProcessing()
    {
        SetupKnownMember();
        _inviteService.ExtractInviteDetailsAsync(Arg.Any<byte[]>(), "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(new BirthdayInviteDetails { IsBirthdayInvite = false });
        _whatsAppService.DownloadMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "", isGroupChat: false,
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

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "", isGroupChat: false,
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
        _conversationRepo.GetByPhoneNumberAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "yes", isGroupChat: false, cancellationToken: CancellationToken.None);

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
        _conversationRepo.GetByPhoneNumberAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "no", isGroupChat: false, cancellationToken: CancellationToken.None);

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
        _conversationRepo.GetByPhoneNumberAsync("+15551234567", Arg.Any<CancellationToken>()).Returns(conversation);

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "Hello", isGroupChat: false, cancellationToken: CancellationToken.None);

        // Should fall through to normal Claude processing
        await _claudeService.Received(1)
            .GetResponseAsync(Arg.Any<FamilyMember>(), Arg.Any<ConversationHistory>(), "Hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleInboundWhatsApp_NonImageMedia_RejectsGracefully()
    {
        SetupKnownMember();

        await _sut.HandleInboundWhatsAppAsync("+15551234567", "", isGroupChat: false,
            mediaUrl: "https://api.twilio.com/media/123", mediaContentType: "application/pdf",
            cancellationToken: CancellationToken.None);

        await _whatsAppService.Received(1)
            .SendMessageAsync("+15551234567", Arg.Is<string>(s => s.Contains("image files")), Arg.Any<CancellationToken>());
        await _inviteService.DidNotReceive()
            .ExtractInviteDetailsAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
