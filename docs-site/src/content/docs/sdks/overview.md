---
title: SDK Overview
description: Typed client SDKs for KynticAI Scout.
---

Scout ships typed SDKs for the two public integration paths currently
present in the repository:

| SDK | Package ID | Source | Current consumption path |
|---|---|---|---|
| TypeScript | `@kynticai/scout-sdk` | `packages/typescript/scout-sdk` | Local package or workspace dependency. |
| .NET | `KynticAI.Scout.Sdk` | `src/KynticAI.Scout.Sdk` | Project reference or local package output. |

Both SDKs wrap the public REST and GraphQL surfaces so application teams do
not need to hand-roll URL construction, auth headers, error envelopes, or
context response types.

## Common Capabilities

| Area | Capability |
|---|---|
| Auth | Interactive login, machine-token exchange, current operator read. |
| Users | User context lookup. |
| Accounts | Account context lookup. |
| Snapshots | Snapshot lookup by ID and latest snapshot helpers. |
| Facts | User and account semantic fact lookup with filters. |
| Selectors | Selector preview and validation. |
| Recompute | Queue user context recomputation. |
| Packages | Governed context and relationship/evidence package retrieval; Scout itself does not call an AI model for this package. |
| Audit | Tenant audit-event lookup. |
| Events | Provider-neutral source-system event ingestion. |

## Choose An SDK

- Use [TypeScript](/sdks/typescript/) for Node.js, browser tooling,
  Vite/React applications, or automation written in TypeScript.
- Use [.NET](/sdks/dotnet/) for C# services, workers, ASP.NET Core apps,
  and Microsoft-stack integrations.

## Publication Status

The repository defines package names and build outputs, but public npm and
NuGet publishing are not yet configured. Use the local package and
project-reference paths shown on the SDK pages until a release process is
explicitly approved.

## Compatibility Notes

The package names and public method shapes are part of the compatibility
surface. Do not rename packages or change SDK method shapes without tests
and compatibility notes.
