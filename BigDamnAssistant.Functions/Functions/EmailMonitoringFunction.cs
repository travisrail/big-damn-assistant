using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Functions.Functions;

public class EmailMonitoringFunction
{
    private readonly IEmailMonitoringService _emailMonitoringService;
    private readonly ILogger<EmailMonitoringFunction> _logger;

    public EmailMonitoringFunction(IEmailMonitoringService emailMonitoringService, ILogger<EmailMonitoringFunction> logger)
    {
        _emailMonitoringService = emailMonitoringService;
        _logger = logger;
    }

    [Function("EmailMonitoring")]
    public async Task Run(
        [TimerTrigger("0 */30 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Email monitoring scan triggered");
        try
        {
            await _emailMonitoringService.ScanMailboxesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email monitoring scan failed");
        }
    }
}
