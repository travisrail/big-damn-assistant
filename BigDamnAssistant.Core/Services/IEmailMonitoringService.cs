namespace BigDamnAssistant.Core.Services;

public interface IEmailMonitoringService
{
    Task ScanMailboxesAsync(CancellationToken cancellationToken = default);
}
