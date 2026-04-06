using System.Net;
using System.Web;
using BigDamnAssistant.Core.Orchestration;
using BigDamnAssistant.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Functions.Functions;

public class WhatsAppWebhookFunction
{
    private readonly MessageOrchestrator _orchestrator;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<WhatsAppWebhookFunction> _logger;

    public WhatsAppWebhookFunction(
        MessageOrchestrator orchestrator,
        IWhatsAppService whatsAppService,
        ILogger<WhatsAppWebhookFunction> logger)
    {
        _orchestrator = orchestrator;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    [Function("WhatsAppWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        // Always return 200 to Twilio to prevent retries
        var okResponse = req.CreateResponse(HttpStatusCode.OK);

        try
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("Empty request body from Twilio");
                return okResponse;
            }

            var formData = HttpUtility.ParseQueryString(body);

            // Validate Twilio signature
            var signature = req.Headers.TryGetValues("X-Twilio-Signature", out var sigValues)
                ? sigValues.FirstOrDefault() ?? string.Empty
                : string.Empty;

            var parameters = formData.AllKeys
                .Where(k => k is not null)
                .ToDictionary(k => k!, k => formData[k] ?? string.Empty);

            if (!_whatsAppService.ValidateRequest(signature, req.Url.ToString(), parameters))
            {
                _logger.LogWarning("Invalid Twilio signature on WhatsApp webhook");
                return okResponse;
            }

            // Twilio sends "Author" for group messages (the actual sender's number).
            // When Author is present, "From" is the group identifier.
            var author = formData["Author"];
            var isGroupChat = !string.IsNullOrEmpty(author);
            var from = isGroupChat
                ? author!.Replace("whatsapp:", "")
                : formData["From"]?.Replace("whatsapp:", "") ?? string.Empty;
            var messageBody = formData["Body"] ?? string.Empty;

            // Extract media attachment info
            var mediaUrl = formData["MediaUrl0"];
            var mediaContentType = formData["MediaContentType0"];

            if (string.IsNullOrEmpty(from))
            {
                _logger.LogWarning("Missing From in WhatsApp webhook");
                return okResponse;
            }

            // Allow messages with media but no text body
            if (string.IsNullOrEmpty(messageBody) && string.IsNullOrEmpty(mediaUrl))
            {
                _logger.LogWarning("Missing Body and MediaUrl in WhatsApp webhook");
                return okResponse;
            }

            await _orchestrator.HandleInboundWhatsAppAsync(
                from, messageBody, isGroupChat, mediaUrl, mediaContentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook");
        }

        return okResponse;
    }
}
