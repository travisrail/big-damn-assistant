using System.Text.Json;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Functions.Functions;

public class PoisonMessageFunction
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<PoisonMessageFunction> _logger;
    private readonly string _adminPhone;

    public PoisonMessageFunction(
        IWhatsAppService whatsAppService,
        ILogger<PoisonMessageFunction> logger)
    {
        _whatsAppService = whatsAppService;
        _logger = logger;
        // Admin phone is the first family member — configured via environment
        _adminPhone = Environment.GetEnvironmentVariable("Assistant:AdminPhone") ?? string.Empty;
    }

    [Function("PoisonMessage")]
    public async Task Run(
        [QueueTrigger("bda-inbound-messages-poison", Connection = "AzureWebJobsStorage")] string messageJson,
        CancellationToken cancellationToken)
    {
        _logger.LogError("Poison message received: {MessageJson}", messageJson);

        if (string.IsNullOrEmpty(_adminPhone))
        {
            _logger.LogWarning("No admin phone configured, cannot send poison message notification");
            return;
        }

        var from = "unknown";
        try
        {
            var message = JsonSerializer.Deserialize<InboundMessage>(messageJson);
            if (message != null)
                from = message.From;
        }
        catch
        {
            // Best effort — use "unknown" if we can't parse
        }

        try
        {
            await _whatsAppService.SendMessageAsync(_adminPhone,
                $"BDA failed to process a message from {from} after multiple retries. Check the logs.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send poison message notification to admin");
        }
    }
}
