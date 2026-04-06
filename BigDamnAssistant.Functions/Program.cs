using Azure.Identity;
using BigDamnAssistant.Core.Configuration;
using BigDamnAssistant.Core.Orchestration;
using BigDamnAssistant.Core.Repositories;
using BigDamnAssistant.Core.Services;
using BigDamnAssistant.Infrastructure;
using BigDamnAssistant.Infrastructure.Repositories;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using Twilio;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // Cosmos DB
        services.AddSingleton(sp =>
        {
            var client = new CosmosClient(config["CosmosDb:ConnectionString"], new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
            return client.GetContainer(config["CosmosDb:DatabaseName"], config["CosmosDb:ContainerName"]);
        });

        // Microsoft Graph
        services.AddSingleton(sp =>
        {
            var tenantId = config["Graph:TenantId"];
            var clientId = config["Graph:ClientId"];
            var clientSecret = config["Graph:ClientSecret"];
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("GraphSetup");

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                logger.LogWarning("Graph credentials not fully configured. TenantId={HasTenant}, ClientId={HasClient}, ClientSecret={HasSecret}",
                    !string.IsNullOrEmpty(tenantId), !string.IsNullOrEmpty(clientId), !string.IsNullOrEmpty(clientSecret));
            }
            else
            {
                logger.LogInformation("Graph client configured for tenant {TenantId}", tenantId);
            }

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential);
        });

        // Twilio
        TwilioClient.Init(config["Twilio:AccountSid"], config["Twilio:AuthToken"]);

        // HTTP clients
        services.AddHttpClient("Claude", client =>
        {
            client.DefaultRequestHeaders.Add("x-api-key", config["Anthropic:ApiKey"]);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        });

        // Configuration
        services.Configure<AssistantOptions>(config.GetSection(AssistantOptions.SectionName));

        // Repositories
        services.AddSingleton<IConversationRepository, CosmosConversationRepository>();
        services.AddSingleton<IFamilyMemberRepository, CosmosFamilyMemberRepository>();
        services.AddSingleton<IReminderRepository, CosmosReminderRepository>();
        services.AddSingleton<IGraphSubscriptionRepository, CosmosGraphSubscriptionRepository>();
        services.AddSingleton<IFamilyMemoryRepository, CosmosFamilyMemoryRepository>();
        services.AddSingleton<IEmailMonitoringRepository, CosmosEmailMonitoringRepository>();
        services.AddSingleton<IAffirmationRepository, CosmosAffirmationRepository>();
        services.AddSingleton<IFeatureRequestRepository, CosmosFeatureRequestRepository>();
        services.AddSingleton<IMemberPreferencesRepository, CosmosMemberPreferencesRepository>();

        // Services
        services.AddSingleton<IClaudeService, ClaudeService>();
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<IWhatsAppService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WhatsAppService>>();
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new WhatsAppService(
                config["Twilio:WhatsAppNumber"] ?? throw new InvalidOperationException("Twilio:WhatsAppNumber not configured"),
                config["Twilio:AccountSid"] ?? throw new InvalidOperationException("Twilio:AccountSid not configured"),
                config["Twilio:AuthToken"] ?? throw new InvalidOperationException("Twilio:AuthToken not configured"),
                httpFactory,
                logger);
        });
        services.AddSingleton<IInviteProcessingService, InviteProcessingService>();
        services.AddSingleton<IFunContentService, FunContentService>();
        services.AddSingleton<IAffirmationService, AffirmationService>();
        services.AddSingleton<IEmailMonitoringService, EmailMonitoringService>();
        services.AddSingleton<ISessionCompressionService, SessionCompressionService>();
        services.AddSingleton<IPreferenceDetectionService, PreferenceDetectionService>();
        services.AddSingleton<ICalendarService>(sp =>
        {
            var graphClient = sp.GetRequiredService<GraphServiceClient>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CalendarService>>();
            return new CalendarService(graphClient, config["Graph:UserId"] ?? throw new InvalidOperationException("Graph:UserId not configured"), logger);
        });
        services.AddSingleton<IMailService>(sp =>
        {
            var graphClient = sp.GetRequiredService<GraphServiceClient>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MailService>>();
            return new MailService(graphClient, config["Graph:UserId"] ?? throw new InvalidOperationException("Graph:UserId not configured"), logger);
        });

        // Orchestration
        services.AddSingleton<MessageOrchestrator>();
    })
    .Build();

host.Run();
