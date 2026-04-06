# Big Damn Assistant (BDA)

A family AI assistant that communicates via WhatsApp, manages a shared family calendar, monitors email, and sends proactive daily briefings. Built on Azure Functions with Anthropic's Claude API.

## What It Does

- **WhatsApp messaging** — Chat with your family assistant via Twilio WhatsApp. Supports direct messages and group chats (with keyword trigger).
- **Calendar management** — Read, create, cancel, and search events on a shared O365 calendar. Invite family members to events.
- **Email monitoring** — Watch family mailboxes for actionable emails from whitelisted senders (schools, doctors, etc.). Summarizes and suggests calendar events or reminders.
- **Morning briefings** — Daily 7 AM message to every family member with the day's calendar, a daily affirmation, and rotating personalized encouragement.
- **Birthday invite scanning** — Send a photo of a birthday party invitation and BDA extracts the details, creates the calendar event, and sets RSVP/gift/day-before reminders.
- **Shared family memory** — "Remember George's shoe size is 9" — any family member can store and retrieve facts.
- **Internet search** — Claude's built-in web search answers questions requiring current information (hours, prices, scores, weather).
- **Fun content** — Send jokes or fun facts (any topic) to other family members via WhatsApp.
- **Daily affirmations** — Family-contributed affirmation pool with rotating personalized messages each morning.
- **Feature requests** — Family members can submit and track ideas for BDA improvements.
- **Weekly recaps** — Sunday evening summary of the week ahead.

## Architecture

```
WhatsApp (Twilio) --> HTTP Trigger --> Orchestrator --> Claude API
                                           |
Graph Webhook -----> HTTP Trigger ---------+
                                           |
Timer Triggers ---> Scheduled Functions ---+
                                           |
                                    +------v-------+
                                    |  Cosmos DB   |
                                    |  Key Vault   |
                                    |  Graph API   |
                                    |  Twilio SDK  |
                                    +--------------+
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) (`npm install -g azure-functions-core-tools@4`)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- An Azure subscription
- An [Anthropic API key](https://console.anthropic.com/)
- A [Twilio account](https://www.twilio.com/) with WhatsApp Business API enabled
- An O365 tenant with at least one mailbox for calendar and email

## Azure Resources

| Resource | Service | Purpose |
|----------|---------|---------|
| Azure Functions | Compute | All application logic (HTTP + timer triggers) |
| Cosmos DB | Database | Conversations, family profiles, reminders, memories, config |
| Key Vault | Secrets | API keys, auth tokens, client secrets |
| Entra ID App Registration | Auth | Microsoft Graph API access (client credentials flow) |
| Application Insights | Monitoring | Logging and telemetry |

## Setup

### 1. Clone the Repository

```bash
git clone https://github.com/YOUR_USERNAME/big-damn-assistant.git
cd big-damn-assistant
```

### 2. Create Azure Resources

```bash
# Login and set subscription
az login
az account set --subscription "YOUR_SUBSCRIPTION"

# Create resource group
az group create --name rg-big-damn-assistant --location centralus

# Create Cosmos DB (serverless)
az cosmosdb create --name cosmos-bda --resource-group rg-big-damn-assistant \
  --capabilities EnableServerless --default-consistency-level Session

az cosmosdb sql database create --account-name cosmos-bda \
  --resource-group rg-big-damn-assistant --name bda

MSYS_NO_PATHCONV=1 az cosmosdb sql container create --account-name cosmos-bda \
  --resource-group rg-big-damn-assistant --database-name bda --name bda \
  --partition-key-path /partitionKey

# Create Key Vault (RBAC authorization)
az keyvault create --name kv-bda --resource-group rg-big-damn-assistant \
  --location centralus --enable-rbac-authorization true

# Create Storage Account (for Functions runtime)
az storage account create --name stbda --resource-group rg-big-damn-assistant \
  --location centralus --sku Standard_LRS

# Create Function App
az functionapp create --name func-bda --resource-group rg-big-damn-assistant \
  --storage-account stbda --consumption-plan-location centralus \
  --runtime dotnet-isolated --runtime-version 8 \
  --functions-version 4 --os-type Linux

# Enable managed identity
az functionapp identity assign --name func-bda \
  --resource-group rg-big-damn-assistant
```

### 3. Configure Key Vault Secrets

Store your secrets in Key Vault:

```bash
az keyvault secret set --vault-name kv-bda --name "anthropic-api-key" --value "YOUR_ANTHROPIC_KEY"
az keyvault secret set --vault-name kv-bda --name "twilio-account-sid" --value "YOUR_TWILIO_SID"
az keyvault secret set --vault-name kv-bda --name "twilio-auth-token" --value "YOUR_TWILIO_TOKEN"
az keyvault secret set --vault-name kv-bda --name "twilio-whatsapp-number" --value "+1XXXXXXXXXX"
az keyvault secret set --vault-name kv-bda --name "cosmosdb-connection-string" --value "YOUR_COSMOS_CONNECTION"
az keyvault secret set --vault-name kv-bda --name "graph-tenant-id" --value "YOUR_TENANT_ID"
az keyvault secret set --vault-name kv-bda --name "graph-client-id" --value "YOUR_CLIENT_ID"
az keyvault secret set --vault-name kv-bda --name "graph-client-secret" --value "YOUR_CLIENT_SECRET"
az keyvault secret set --vault-name kv-bda --name "graph-user-id" --value "calendar@yourdomain.com"
```

Grant the Function App's managed identity access to Key Vault:

```bash
PRINCIPAL_ID=$(az functionapp identity show --name func-bda \
  --resource-group rg-big-damn-assistant --query principalId -o tsv)

KV_ID=$(az keyvault show --name kv-bda --resource-group rg-big-damn-assistant \
  --query id -o tsv)

az role assignment create --assignee $PRINCIPAL_ID \
  --role "Key Vault Secrets User" --scope $KV_ID
```

### 4. Configure Function App Settings

```bash
az functionapp config appsettings set --name func-bda \
  --resource-group rg-big-damn-assistant --settings \
  "CosmosDb__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/cosmosdb-connection-string/)" \
  "CosmosDb__DatabaseName=bda" \
  "CosmosDb__ContainerName=bda" \
  "Graph__TenantId=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/graph-tenant-id/)" \
  "Graph__ClientId=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/graph-client-id/)" \
  "Graph__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/graph-client-secret/)" \
  "Graph__UserId=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/graph-user-id/)" \
  "Twilio__AccountSid=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/twilio-account-sid/)" \
  "Twilio__AuthToken=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/twilio-auth-token/)" \
  "Twilio__WhatsAppNumber=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/twilio-whatsapp-number/)" \
  "Anthropic__ApiKey=@Microsoft.KeyVault(SecretUri=https://kv-bda.vault.azure.net/secrets/anthropic-api-key/)" \
  "Assistant__Name=Big Damn Assistant" \
  "Assistant__TriggerKeyword=BDA"
```

> **Note:** Azure Functions uses `__` (double underscore) as the section separator in app settings. These map to `:` in .NET configuration (e.g., `Graph__TenantId` becomes `Graph:TenantId`).

### 5. Create Entra ID App Registration

This is required for Microsoft Graph API access to calendars and email.

1. Go to **Azure Portal > Entra ID > App registrations > New registration**
2. Name it something like "BDA Graph Access"
3. Add **Application permissions** (not delegated):
   - `Mail.ReadWrite`
   - `Mail.Send`
   - `Calendars.ReadWrite`
4. **Grant admin consent** for all permissions
5. Create a **client secret** and store it in Key Vault as `graph-client-secret`
6. Copy the **Application (client) ID** to Key Vault as `graph-client-id`
7. Copy your **Tenant ID** to Key Vault as `graph-tenant-id`
8. Set `graph-user-id` to the UPN of the mailbox BDA will use (e.g., `calendar@yourdomain.com`)

### 6. Configure Twilio WhatsApp

1. Set up a [Twilio WhatsApp Sender](https://www.twilio.com/docs/whatsapp) (sandbox for testing, Business profile for production)
2. Get your Function App's webhook URL:
   ```bash
   # Get the function key
   az functionapp function keys list --name func-bda \
     --resource-group rg-big-damn-assistant \
     --function-name WhatsAppWebhook -o json
   ```
3. Set the webhook URL in Twilio as:
   ```
   https://func-bda.azurewebsites.net/api/whatsappwebhook?code=YOUR_FUNCTION_KEY
   ```
4. Set this as the **"When a message comes in"** webhook (POST) in your Twilio WhatsApp sender configuration

### 7. Add Family Members

Family members are stored in Cosmos DB. Add them manually or via the Azure Portal Data Explorer:

```json
{
  "id": "member-+15551234567",
  "partitionKey": "members",
  "type": "familyMember",
  "name": "Travis",
  "phoneNumber": "+15551234567",
  "email": "travis@example.com",
  "nicknames": ["dad", "daddy"],
  "role": "admin",
  "timezone": "America/Chicago"
}
```

- `phoneNumber` must be in E.164 format (`+1XXXXXXXXXX`)
- `timezone` uses IANA timezone IDs (e.g., `America/Chicago`, `America/New_York`)
- `nicknames` allow other members to say "send dad a joke" instead of "send Travis a joke"

### 8. Build and Deploy

```bash
# Build
dotnet build BigDamnAssistant/BigDamnAssistant.sln

# Run tests
dotnet test BigDamnAssistant/BigDamnAssistant.Tests/

# Deploy
cd BigDamnAssistant/BigDamnAssistant.Functions
func azure functionapp publish func-bda

# Restart to pick up new settings
az functionapp restart --name func-bda --resource-group rg-big-damn-assistant
```

### 9. Verify Deployment

```bash
# List deployed functions
az functionapp function list --name func-bda \
  --resource-group rg-big-damn-assistant --query "[].name" -o tsv
```

You should see:
```
EmailMonitoring
GraphSubscriptionRenewal
GraphWebhook
MorningBriefing
WeeklyRecap
WhatsAppWebhook
```

Send a WhatsApp message to your BDA number to test.

## Local Development

1. Copy `local.settings.json.example` to `local.settings.json` and fill in your values
2. Start the [Cosmos DB Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/local-emulator)
3. Run:
   ```bash
   cd BigDamnAssistant/BigDamnAssistant.Functions
   func start
   ```
4. Use [ngrok](https://ngrok.com/) to expose your local endpoint for Twilio webhooks

## Configuration Reference

| Setting | Description | Example |
|---------|-------------|---------|
| `CosmosDb:ConnectionString` | Cosmos DB connection string | `AccountEndpoint=https://...` |
| `CosmosDb:DatabaseName` | Database name | `bda` |
| `CosmosDb:ContainerName` | Container name | `bda` |
| `Graph:TenantId` | Entra ID tenant ID | `xxxxxxxx-xxxx-...` |
| `Graph:ClientId` | App registration client ID | `xxxxxxxx-xxxx-...` |
| `Graph:ClientSecret` | App registration client secret | (from Key Vault) |
| `Graph:UserId` | O365 mailbox UPN or Object ID | `calendar@yourdomain.com` |
| `Twilio:AccountSid` | Twilio account SID | `AC...` |
| `Twilio:AuthToken` | Twilio auth token | (from Key Vault) |
| `Twilio:WhatsAppNumber` | BDA's WhatsApp number (E.164) | `+15551234567` |
| `Anthropic:ApiKey` | Claude API key | `sk-ant-...` |
| `Assistant:Name` | Display name for the assistant | `Big Damn Assistant` |
| `Assistant:TriggerKeyword` | Keyword for group chat activation | `BDA` |

## Cosmos DB Design

Single container (`bda`) with partition key `/partitionKey`. All documents include a `type` discriminator field.

| Document Type | Partition Key | Purpose |
|---------------|---------------|---------|
| `familyMember` | `members` | Family member profiles |
| `conversation` | `conv-{phone}` | Per-member chat history (last 20 messages) |
| `reminder` | `reminders` | Scheduled reminders (TTL auto-delete) |
| `graphSubscription` | `system` | Graph webhook subscription state |
| `familyMemory` | `familyMemory` | Shared family knowledge base |
| `affirmationPool` | `affirmations` | Family-contributed affirmations |
| `affirmationRotation` | `system` | Daily rotation state |
| `monitoredMailbox` | `emailMonitoring` | Watched mailboxes |
| `whitelistedSender` | `emailMonitoring` | Approved email senders |
| `mailboxScanState` | `emailMonitoring` | Email scan watermarks |
| `emailActionPending` | `emailMonitoring` | Pending email action confirmations |
| `featureRequest` | `featureRequests` | Family feature request tracking |

## WhatsApp Commands

| Command | What It Does |
|---------|-------------|
| Ask anything | Claude responds conversationally with full tool access |
| "What's on the calendar this week?" | Searches calendar events |
| "Create an event..." | Creates a calendar event with optional invites |
| "Cancel the dentist appointment" | Finds and cancels the event |
| "Remember George's shoe size is 9" | Saves to family memory |
| "What do you remember?" | Lists all family memories |
| "Send dad a joke" | Generates and sends a joke to another member |
| "Send mom a space fact" | Generates and sends a topic-specific fun fact |
| "Search for pizza places near me" | Web search for current information |
| "Add affirmation: Every day is a gift" | Adds to the morning affirmation pool |
| "Monitor sarah@family.com" | Adds a mailbox to email monitoring |
| "Watch emails from @school.edu" | Whitelists a sender domain |
| "Add feature request: ..." | Logs a feature idea |
| "Show feature requests" | Lists all feature requests |
| Send a photo of a birthday invite | Extracts details, offers to create event + reminders |

## Timer Functions

| Function | Schedule | Description |
|----------|----------|-------------|
| Morning Briefing | 7:00 AM CT daily | Calendar + affirmation for all members |
| Weekly Recap | 6:00 PM CT Sunday | Week-ahead calendar summary |
| Email Monitoring | Every 30 minutes | Scans whitelisted senders in monitored mailboxes |
| Graph Subscription Renewal | Every 2 days | Renews Graph mail webhook (3-day expiry) |

To manually trigger any timer function:

```bash
curl -X POST "https://func-bda.azurewebsites.net/admin/functions/MorningBriefing" \
  -H "x-functions-key: YOUR_MASTER_KEY" \
  -H "Content-Type: application/json" -d '{}'
```

## Technology Stack

| Concern | Choice |
|---------|--------|
| Runtime | Azure Functions v4, .NET 8 isolated worker |
| Language | C# |
| AI | Anthropic Claude API (claude-sonnet-4-20250514) |
| Messaging | Twilio WhatsApp Business API |
| Email + Calendar | Microsoft Graph API |
| Database | Azure Cosmos DB NoSQL (serverless) |
| Secrets | Azure Key Vault via Managed Identity |
| Auth | Entra ID app registration, client credentials flow |
| Testing | xUnit + NSubstitute |

## Cost Considerations

This project is designed for family-scale usage. With Azure serverless pricing:

- **Azure Functions** — Consumption plan, first 1M executions/month free
- **Cosmos DB** — Serverless capacity, pay per RU consumed (very low at family scale)
- **Key Vault** — Minimal cost for secret reads
- **Claude API** — Pay per token. Web search tool incurs additional per-invocation cost
- **Twilio** — Per-message pricing for WhatsApp

Estimated monthly cost for a family of 4-6 with moderate usage: **$5-15/month** (primarily Claude API and Twilio).

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests: `dotnet test BigDamnAssistant/BigDamnAssistant.Tests/`
5. Submit a pull request

## License

MIT
