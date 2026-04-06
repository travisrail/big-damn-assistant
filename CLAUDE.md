# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Big Damn Assistant (BDA)** — a family AI assistant that communicates via WhatsApp, reads/sends email through a dedicated O365 mailbox, and manages a shared family calendar. This document is the authoritative guide for all development decisions. Follow it without deviation unless explicitly told otherwise.

**What it does:**
- Receives and sends WhatsApp messages via Twilio
- Reads inbound email to the BDA O365 mailbox and takes action (reply, summarize, notify family via WhatsApp)
- Creates, reads, and updates calendar events via Microsoft Graph
- Sends proactive messages (morning briefings, reminders, weekly summaries) on a schedule
- Maintains per-family-member conversation context

**What it is not:**
- A public-facing API
- A multi-tenant SaaS product
- A replacement for individual family members' personal mailboxes

---

## Architecture

```
WhatsApp (Twilio) ──► HTTP Trigger Function ──► Orchestrator ──► Claude API
                                                      │
Graph Webhook ────► HTTP Trigger Function ────────────┤
                                                      │
Timer Triggers ──► Scheduled Function ────────────────┤
                                                      │
                                              ┌───────▼────────┐
                                              │  Cosmos DB     │
                                              │  Key Vault     │
                                              │  Graph API     │
                                              │  Twilio SDK    │
                                              └────────────────┘
```

### Azure Resources
- **Azure Functions** — .NET 8 isolated worker, all compute lives here
- **Cosmos DB** — conversation history, family member profiles, reminder documents, Graph subscription state
- **Azure Key Vault** — all secrets (Twilio auth, Anthropic API key, Graph client secret)
- **Managed Identity** — used by Functions to access Key Vault; never hardcode credentials
- **Microsoft Graph API** — O365 mailbox (Mail.ReadWrite) and calendar (Calendars.ReadWrite)

---

## Technology Stack

| Concern | Choice |
|---|---|
| Runtime | Azure Functions v4, .NET 8 isolated worker |
| Language | C# |
| AI | Anthropic Claude API (claude-sonnet-4-20250514) |
| Messaging | Twilio WhatsApp Business API |
| Email + Calendar | Microsoft Graph API (`Microsoft.Graph` NuGet) |
| Database | Azure Cosmos DB (NoSQL, single container) |
| Secrets | Azure Key Vault via Managed Identity |
| Auth (Graph) | Entra ID app registration, client credentials flow |
| HTTP Client | `IHttpClientFactory` — always, no raw `new HttpClient()` |

---

## Project Structure

```
BigDamnAssistant/
├── BigDamnAssistant.Functions/        # Azure Functions project
│   ├── Functions/
│   │   ├── WhatsAppWebhookFunction.cs
│   │   ├── GraphWebhookFunction.cs
│   │   ├── MorningBriefingFunction.cs
│   │   ├── WeeklyRecapFunction.cs
│   │   └── GraphSubscriptionRenewalFunction.cs
│   ├── Program.cs
│   └── host.json
├── BigDamnAssistant.Core/             # Business logic, no Azure dependencies
│   ├── Orchestration/
│   │   └── MessageOrchestrator.cs
│   ├── Services/
│   │   ├── ClaudeService.cs
│   │   ├── CalendarService.cs
│   │   ├── MailService.cs
│   │   ├── WhatsAppService.cs
│   │   └── ReminderService.cs
│   ├── Models/
│   │   ├── FamilyMember.cs
│   │   ├── ConversationHistory.cs
│   │   ├── ReminderDocument.cs
│   │   └── GraphSubscriptionDocument.cs
│   └── Repositories/
│       ├── IConversationRepository.cs
│       └── IFamilyMemberRepository.cs
├── BigDamnAssistant.Infrastructure/   # Cosmos, Graph, Twilio implementations
│   └── Repositories/
│       ├── CosmosConversationRepository.cs
│       └── CosmosFamilyMemberRepository.cs
└── BigDamnAssistant.Tests/            # xUnit tests
    ├── Orchestration/
    └── Services/
```

---

## Cosmos DB Design

**Single container: `bda`**, partition key: `/partitionKey`

### Document Types

All documents include a `type` discriminator field.

**Family Member Profile**
```json
{
  "id": "member-{phoneNumber}",
  "partitionKey": "members",
  "type": "familyMember",
  "name": "Travis",
  "phoneNumber": "+1xxxxxxxxxx",
  "role": "admin",
  "timezone": "America/Chicago"
}
```

**Conversation History**
```json
{
  "id": "conv-{phoneNumber}",
  "partitionKey": "conv-{phoneNumber}",
  "type": "conversation",
  "phoneNumber": "+1xxxxxxxxxx",
  "messages": [],
  "updatedAt": "2026-01-01T00:00:00Z"
}
```

**Reminder Document** (Change Feed driven)
```json
{
  "id": "reminder-{guid}",
  "partitionKey": "reminders",
  "type": "reminder",
  "targetPhoneNumber": "+1xxxxxxxxxx",
  "message": "Dentist appointment in 2 hours",
  "fireAt": "2026-01-15T14:00:00Z",
  "ttl": 86400,
  "processed": false
}
```

**Graph Subscription State**
```json
{
  "id": "graphsub-mail",
  "partitionKey": "system",
  "type": "graphSubscription",
  "subscriptionId": "...",
  "expiresAt": "2026-01-04T00:00:00Z",
  "resource": "me/mailFolders/inbox/messages"
}
```

---

## Coding Standards

### General
- All service classes must be registered via DI in `Program.cs` — no `new` instantiation of services
- Use `IHttpClientFactory` for all HTTP clients
- Never log secrets, phone numbers, or message content at Info level or above — use Debug only and assume logs are observable
- All external API calls must have try/catch with structured error logging
- Use `CancellationToken` on all async methods that call external services

### Naming
- Functions: `{Trigger}Function.cs` (e.g., `WhatsAppWebhookFunction.cs`)
- Services: `{Domain}Service.cs`
- Repositories: `Cosmos{Domain}Repository.cs` implementing `I{Domain}Repository`
- Cosmos document models: suffix with `Document` only if they are raw persistence models

### Secrets
- All secrets come from Key Vault via Managed Identity
- Reference in `local.settings.json` for local dev using the Key Vault reference syntax: `@Microsoft.KeyVault(SecretUri=...)`
- Never hardcode secrets, connection strings, or API keys anywhere — not even in comments

### Error Handling
- Twilio webhook functions must return HTTP 200 even on internal errors — Twilio will retry on non-200 and cause duplicate messages
- Graph webhook validation requests (containing `validationToken`) must be handled before any other logic and return the token as plain text with HTTP 200
- Wrap Claude API calls with a fallback response so a Claude outage does not silently drop messages

---

## Claude API Usage

- Model: `claude-sonnet-4-20250514` — do not change without explicit instruction
- Always include a system prompt that establishes BDA's role and the current family member context
- Pass the full conversation history from Cosmos on every call (last 20 messages maximum)
- After each response, persist the updated history back to Cosmos immediately
- Claude responses drive all decisions — do not add post-processing logic that second-guesses the response

### System Prompt Pattern
```
You are Big Damn Assistant, a helpful family AI assistant for the {FamilyName} family.
You are currently speaking with {MemberName}.
Today is {Date}. Local time is {LocalTime} ({Timezone}).
You have access to the family calendar and the shared BDA email inbox.
Be warm, concise, and practical. You are not a chatbot — you are a capable assistant.
```

---

## Microsoft Graph Integration

- Auth: client credentials flow via Entra ID app registration
- Permissions required: `Mail.ReadWrite`, `Calendars.ReadWrite`, `Mail.Send`
- Use `GraphServiceClient` from `Microsoft.Graph` — do not hand-roll Graph REST calls
- Graph subscriptions for inbound mail expire every 3 days — the `GraphSubscriptionRenewalFunction` timer runs every 2 days to renew
- Store subscription state in Cosmos (`graphsub-mail` document) so renewal is idempotent

---

## Twilio WhatsApp Integration

- Inbound messages POST to `WhatsAppWebhookFunction` — validate Twilio signature on every request using `RequestValidator`
- Outbound messages use the Twilio C# SDK — do not use raw HTTP
- Phone numbers stored in E.164 format everywhere (`+1xxxxxxxxxx`)
- The BDA WhatsApp number is stored in Key Vault as `twilio-whatsapp-number`

---

## Testing

Tests live in `BigDamnAssistant.Tests` using xUnit.

**Write tests for:**
- `MessageOrchestrator` — routing logic, decision branching
- `ClaudeService` — prompt construction, history trimming, response parsing
- `CalendarService` — event creation/update logic, conflict detection
- `ReminderService` — document creation, fireAt calculation
- Repository implementations — use CosmosDB emulator or mocked `CosmosClient`

**Do not write tests for:**
- Azure Function trigger classes (thin wiring only — no logic)
- Configuration/DI setup
- Twilio or Graph SDK calls (mock the service interfaces instead)

Use `NSubstitute` for mocking. Do not use Moq.

---

## Build & Test Commands

```bash
# Build entire solution
dotnet build BigDamnAssistant/BigDamnAssistant.sln

# Run all tests
dotnet test BigDamnAssistant/BigDamnAssistant.Tests/

# Run a single test by fully qualified name
dotnet test BigDamnAssistant/BigDamnAssistant.Tests/ --filter "FullyQualifiedName~ClassName.MethodName"

# Run tests in a specific class
dotnet test BigDamnAssistant/BigDamnAssistant.Tests/ --filter "FullyQualifiedName~Orchestration.MessageOrchestratorTests"

# Run the Functions app locally (requires Azure Functions Core Tools)
cd BigDamnAssistant/BigDamnAssistant.Functions && func start
```

---

## Local Development

- Use `local.settings.json` (gitignored) for local config
- Cosmos DB Emulator for local persistence
- Twilio dev number for WhatsApp testing (separate from production number)
- Never point local dev at the production BDA mailbox
- `func start` from the Functions project directory

---

## What Not To Do

- Do not add features to the Functions project that belong in Core
- Do not call `Thread.Sleep` — use `Task.Delay` with cancellation
- Do not swallow exceptions silently — always log with context
- Do not store conversation history in memory — always Cosmos
- Do not skip Graph subscription signature validation
- Do not send WhatsApp messages directly from timer functions without checking quiet hours (10pm–7am CT)
