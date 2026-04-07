using System.Text.Json;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Orchestration;
using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Functions.Functions;

public class MessageProcessingFunction
{
    private readonly MessageOrchestrator _orchestrator;
    private readonly ITypingIndicatorService _typingIndicatorService;
    private readonly ILogger<MessageProcessingFunction> _logger;

    public MessageProcessingFunction(
        MessageOrchestrator orchestrator,
        ITypingIndicatorService typingIndicatorService,
        ILogger<MessageProcessingFunction> logger)
    {
        _orchestrator = orchestrator;
        _typingIndicatorService = typingIndicatorService;
        _logger = logger;
    }

    [Function("MessageProcessing")]
    public async Task Run(
        [QueueTrigger("bda-inbound-messages", Connection = "AzureWebJobsStorage")] string messageJson,
        CancellationToken cancellationToken)
    {
        InboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<InboundMessage>(messageJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize queue message");
            throw; // Let it go to poison queue
        }

        if (message == null)
        {
            _logger.LogError("Deserialized queue message was null");
            throw new InvalidOperationException("Queue message deserialized to null");
        }

        _logger.LogInformation("Processing message {MessageSid} from {From} on {Channel}",
            message.MessageSid, message.From, message.Channel);

        // Send typing indicator — non-blocking on failure
        try
        {
            await _typingIndicatorService.SendAsync(message.MessageSid, message.Channel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Typing indicator failed for {MessageSid} — continuing", message.MessageSid);
        }

        await _orchestrator.ProcessAsync(message, cancellationToken);
    }
}
