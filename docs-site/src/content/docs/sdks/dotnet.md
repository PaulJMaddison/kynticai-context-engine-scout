---
title: .NET SDK
description: Using the KynticAI Scout .NET SDK from C# applications.
---

The .NET SDK lives in `src/KynticAI.Scout.Sdk` and uses the package ID
`KynticAI.Scout.Sdk` when packed locally. Public NuGet publishing is not yet
configured; use the project-reference path below until a release process is
explicitly approved.

## Add A Project Reference

```bash
dotnet add reference src/KynticAI.Scout.Sdk/KynticAI.Scout.Sdk.csproj
```

## Create A Client

```csharp
using KynticAI.Scout.Sdk;

using var scout = new ScoutClient(new ScoutClientOptions
{
    BaseUrl = "http://127.0.0.1:5198",
    AccessToken = Environment.GetEnvironmentVariable("SCOUT_TOKEN")
});
```

## Machine Token Flow

```csharp
using KynticAI.Scout.Sdk;

using var bootstrap = new ScoutClient(new ScoutClientOptions
{
    BaseUrl = "http://127.0.0.1:5198"
});

var token = await bootstrap.Auth.GetMachineTokenAsync(
    new MachineTokenRequest(
        "client_credentials",
        Environment.GetEnvironmentVariable("SCOUT_CLIENT_ID")!,
        Environment.GetEnvironmentVariable("SCOUT_CLIENT_SECRET")!,
        "context:read context:write audit:read"));

using var scout = new ScoutClient(new ScoutClientOptions
{
    BaseUrl = "http://127.0.0.1:5198",
    AccessToken = token.AccessToken
});
```

## Context Reads

```csharp
var user = await scout.Users.GetContextAsync("demo", "123");
var account = await scout.Accounts.GetContextAsync("demo", "acct-123");

var facts = await scout.Facts.GetForUserAsync(
    "demo",
    "123",
    new ContextFactLookupOptions("health", 1, 25));

var accountFacts = await scout.Facts.GetForAccountAsync("demo", "acct-123");

var snapshot = user is null
    ? null
    : await scout.Snapshots.GetByIdAsync("demo", user.SnapshotId);
```

## Events And Recompute

```csharp
await scout.Events.IngestSourceSystemEventAsync(
    "demo",
    new SourceSystemEventRequest(
        EventId: "evt-demo-001",
        WorkspaceSlug: null,
        SourceSystem: "product",
        EventType: "source.product_usage.rollup_ready",
        Payload: new Dictionary<string, object?> { ["activeDays30"] = 22 },
        PayloadJson: null,
        ExternalUserId: "123",
        ExternalAccountId: null,
        ObservedAtUtc: null));

await scout.Recompute.QueueForUserAsync("demo", "123", "product-webhook");
```

## Context Packages

```csharp
var contextPackage = await scout.Packages.GetAiContextForUserAsync(
    "demo",
    "123",
    "Prepare a renewal-risk brief for the account team.");
```

Scout returns a grounded context package for a downstream consumer. The SDK
does not make model-provider calls.

## Tenant-Scoped Client

```csharp
var demo = scout.ForTenant("demo");

var user = await demo.Users.GetContextAsync("123");
var audit = await demo.Audit.GetEventsAsync();
```

## Build And Test Locally

```bash
dotnet build src/KynticAI.Scout.Sdk/KynticAI.Scout.Sdk.csproj
dotnet test KynticAI.Scout.slnx
```

See [REST API](/apis/rest/) and [GraphQL API](/apis/graphql/) for the
underlying server surfaces.
