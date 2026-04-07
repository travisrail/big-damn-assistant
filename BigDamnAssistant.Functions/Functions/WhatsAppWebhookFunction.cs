using System.Net;
using System.Web;
using BigDamnAssistant.Core.Models;
using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Functions.Functions;

public class WhatsAppWebhookFunction
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IQueueService _queueService;
    private readonly ILogger<WhatsAppWebhookFunction> _logger;

    public WhatsAppWebhookFunction(
        IWhatsAppService whatsAppService,
        IQueueService queueService,
        ILogger<WhatsAppWebhookFunction> logger)
    {
        _whatsAppService = whatsAppService;
        _queueService = queueService;
        _logger = logger;
    }

    [Function("WhatsAppWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
        {
            _logger.LogWarning("Empty request body from Twilio");
            return CreateEmptyTwimlResponse(req);
        }

        // Validate Twilio signature
        var formData = HttpUtility.ParseQueryString(body);
        var signature = req.Headers.TryGetValues("X-Twilio-Signature", out var sigValues)
            ? sigValues.FirstOrDefault() ?? string.Empty
            : string.Empty;

        var parameters = formData.AllKeys
            .Where(k => k is not null)
            .ToDictionary(k => k!, k => formData[k] ?? string.Empty);

        if (!_whatsAppService.ValidateRequest(signature, req.Url.ToString(), parameters))
        {
            _logger.LogWarning("Invalid Twilio signature on WhatsApp webhook");
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            return forbidden;
        }

        // Ignore Twilio delivery status callbacks (outbound statuses like sent/delivered/failed)
        var messageStatus = formData["MessageStatus"] ?? formData["SmsStatus"] ?? "";
        if (!string.IsNullOrEmpty(messageStatus)
            && !messageStatus.Equals("received", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ignoring Twilio status callback: {Status}", messageStatus);
            return CreateEmptyTwimlResponse(req);
        }

        // Detect channel from Twilio To field
        var to = formData["To"] ?? string.Empty;
        var channel = to.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? MessageChannel.WhatsApp
            : MessageChannel.SMS;

        // Twilio sends "Author" for group messages (the actual sender's number)
        var author = formData["Author"];
        var isGroupChat = channel == MessageChannel.WhatsApp && !string.IsNullOrEmpty(author);
        var from = isGroupChat
            ? author!.Replace("whatsapp:", "")
            : formData["From"]?.Replace("whatsapp:", "") ?? string.Empty;
        var messageBody = formData["Body"] ?? string.Empty;
        var mediaUrl = formData["MediaUrl0"];
        var mediaContentType = formData["MediaContentType0"];
        var messageSid = formData["MessageSid"] ?? string.Empty;

        if (string.IsNullOrEmpty(from))
        {
            _logger.LogDebug("Ignoring callback with no From field (likely delivery status)");
            return CreateEmptyTwimlResponse(req);
        }

        if (string.IsNullOrEmpty(messageBody) && string.IsNullOrEmpty(mediaUrl))
        {
            _logger.LogWarning("Missing Body and MediaUrl in WhatsApp webhook");
            return CreateEmptyTwimlResponse(req);
        }

        // Enqueue for async processing
        var inboundMessage = new InboundMessage
        {
            MessageSid = messageSid,
            From = from,
            Body = messageBody,
            Channel = channel,
            IsGroupChat = isGroupChat,
            MediaUrl = mediaUrl,
            MediaContentType = mediaContentType,
            ReceivedAt = DateTime.UtcNow
        };

        try
        {
            await _queueService.EnqueueAsync("bda-inbound-messages", inboundMessage, cancellationToken);
            _logger.LogInformation("Enqueued inbound message {MessageSid} from {From} on {Channel}",
                messageSid, from, channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue message {MessageSid}", messageSid);
        }

        // Return empty TwiML immediately
        return CreateEmptyTwimlResponse(req);
    }

    private static HttpResponseData CreateEmptyTwimlResponse(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/xml");
        response.WriteString("<Response></Response>");
        return response;
    }
}
