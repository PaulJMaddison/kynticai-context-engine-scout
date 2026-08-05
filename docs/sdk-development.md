# SDK Development

KynticAI Scout includes two SDK scaffolds so consuming products can integrate against stable client interfaces instead of hand-rolling GraphQL and REST requests during pilots.

They are not currently configured for public package publishing. Treat NuGet/npm publishing as a deliberate later release task, with the product boundary reviewed first.

## Layout

```text
src/KynticAI.Scout.Sdk/
tests/KynticAI.Scout.Sdk.Tests/
packages/typescript/scout-sdk/
```

## Public Surface

Both SDKs expose equivalent capability groups:

- `auth`
- `users`
- `accounts`
- `snapshots`
- `facts`
- `selectors`
- `recompute`
- `packages`
- `audit`
- tenant-scoped clients via `forTenant(...)`

## Local Development

### .NET

```bash
# Linux / macOS
./.dotnet/dotnet test tests/KynticAI.Scout.Sdk.Tests/KynticAI.Scout.Sdk.Tests.csproj
./.dotnet/dotnet pack src/KynticAI.Scout.Sdk/KynticAI.Scout.Sdk.csproj -c Release

# Windows
.\.dotnet\dotnet.exe test tests\KynticAI.Scout.Sdk.Tests\KynticAI.Scout.Sdk.Tests.csproj
.\.dotnet\dotnet.exe pack src\KynticAI.Scout.Sdk\KynticAI.Scout.Sdk.csproj -c Release
```

### TypeScript

```bash
cd packages/typescript/scout-sdk
npm install
npm run build
npm test
npm run pack:dry-run
```

## Versioning

- keep npm and NuGet SDK versions aligned to the private product line
- minor releases can add new client groups, methods, or response fields
- major releases are reserved for breaking contract changes

## Packaging

- NuGet: `KynticAI.Scout.Sdk`
- npm: `@kynticai/scout-sdk`
- current packaging commands are local validation aids, not release publishing steps

## Test Coverage

Recommended coverage for both SDKs:

- authentication request formatting
- request tracing header injection
- transient retry handling
- GraphQL error propagation
- problem-details REST error propagation
- tenant-scoped client delegation
- representative REST v1 user, account, and snapshot context queries

## Compatibility Notes

### Fact value types (2.8.0)

- The API serialises `ContextFactResult.valueType` as an integer (1 = String, 2 = Number, 3 = Boolean, 4 = Json, 5 = Enum, 6 = EnumSet).
- .NET SDK: `valueType` is now the `KynticAI.Scout.Sdk.FactValueType` enum. The SDK registers a tolerant JSON converter that accepts the API's integer encoding (and a string alias for interoperability), so existing consumers do not need to change how they deserialise. Consumers that previously compared `valueType` against a free-form string must switch to the enum; the wire values are pinned by contract tests in `tests/KynticAI.Scout.Sdk.Tests`.
- TypeScript SDK: `valueType` is now the `FactValueType` string-literal union (`'string' | 'number' | 'boolean' | 'json' | 'enum' | 'enumSet'`), matching the API's JSON representation.
- The .NET SDK package remains self-contained: it has no dependency on other KynticAI.Scout assemblies.
