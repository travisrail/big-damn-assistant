using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using NSubstitute;

namespace BigDamnAssistant.Tests.Services;

public class ReminderServiceTests
{
    private readonly IReminderRepository _repo = Substitute.For<IReminderRepository>();
    private readonly ReminderService _sut;

    public ReminderServiceTests()
    {
        _sut = new ReminderService(_repo);
    }

    [Fact]
    public async Task CreateReminder_CreatesDocumentWithCorrectFields()
    {
        var fireAt = DateTimeOffset.UtcNow.AddHours(2);

        await _sut.CreateReminderAsync("+15551234567", "Dentist appointment", fireAt);

        await _repo.Received(1).CreateAsync(
            Arg.Is<ReminderDocument>(r =>
                r.TargetPhoneNumber == "+15551234567" &&
                r.Message == "Dentist appointment" &&
                r.FireAt == fireAt &&
                r.Processed == false &&
                r.Id.StartsWith("reminder-")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDueReminders_DelegatesToRepository()
    {
        var reminders = new List<ReminderDocument>
        {
            new() { Id = "reminder-1", Message = "Test" }
        };

        _repo.GetPendingRemindersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(reminders);

        var result = await _sut.GetDueRemindersAsync();

        Assert.Single(result);
        Assert.Equal("reminder-1", result[0].Id);
    }

    [Fact]
    public async Task MarkProcessed_DelegatesToRepository()
    {
        await _sut.MarkProcessedAsync("reminder-1");

        await _repo.Received(1).MarkProcessedAsync("reminder-1", Arg.Any<CancellationToken>());
    }
}
