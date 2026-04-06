using System.Net;
using System.Text.Json;
using BigDamnAssistant.Core.Orchestration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace BigDamnAssistant.Functions.Functions;

public class GraphWebhookFunction
{
    private readonly MessageOrchestrator _orchestrator;
    private readonly ILogger<GraphWebhookFunction> _logger;

    public GraphWebhookFunction(MessageOrchestrator orchestrator, ILogger<GraphWebhookFunction> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [Function("GraphWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        // Handle Graph subscription validation
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var validationToken = query["validationToken"];
        if (!string.IsNullOrEmpty(validationToken))
        {
            var validationResponse = req.CreateResponse(HttpStatusCode.OK);
            validationResponse.Headers.Add("Content-Type", "text/plain");
            await validationResponse.WriteStringAsync(validationToken, cancellationToken);
            return validationResponse;
        }

        try
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                return req.CreateResponse(HttpStatusCode.OK);
            }

            var notification = JsonSerializer.Deserialize<GraphNotificationPayload>(body);
            if (notification?.Value is not null)
            {
                foreach (var item in notification.Value)
                {
                    if (!string.IsNullOrEmpty(item.ResourceData?.Id))
                    {
                        await _orchestrator.HandleInboundEmailAsync(item.ResourceData.Id, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Graph webhook notification");
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }
}

internal class GraphNotificationPayload
{
    public List<GraphNotificationItem>? Value { get; set; }
}

internal class GraphNotificationItem
{
    public GraphResourceData? ResourceData { get; set; }
}

internal class GraphResourceData
{
    public string? Id { get; set; }
}
