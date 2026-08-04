#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

fail() { echo "Pilot readiness failed: $1" >&2; exit 1; }
step() { echo "==> $1"; }

step "No GitHub Actions workflows"
if [[ -d .github/workflows ]] && find .github/workflows -type f \( -name '*.yml' -o -name '*.yaml' \) | grep -q .; then
  fail ".github/workflows must not contain active workflow files in the public repo."
fi

step "Tracked runtime artefact scan"
unsafe="$(git ls-files | grep -Ei '(^|/)(\.env(\.local)?|.*\.(db|sqlite|sqlite3|log|pem|key|pfx|p12|crt|cer)|.*\.lic|.*\.licence\.json|node_modules|bin/|obj/|dist/|support-bundle)' | grep -Evi '(\.env\.example$|^docs/|LICENSE)' || true)"
[[ -z "$unsafe" ]] || { echo "$unsafe"; fail "tracked runtime artefacts or secrets were found."; }

step "Production example toggles"
grep -q 'VITE_DEMO_FALLBACK=false' .env.example || fail ".env.example must set VITE_DEMO_FALLBACK=false."
grep -q 'VITE_DEMO_FALLBACK=false' apps/web/.env.example || fail "apps/web/.env.example must set VITE_DEMO_FALLBACK=false."
grep -q '"SeedDemoData": false' src/KynticAI.Scout.Api/appsettings.Production.json || fail "Production appsettings must keep Bootstrap:SeedDemoData=false."

step "Backend build"
dotnet build ./KynticAI.Scout.slnx

step "Focused backend tests"
dotnet test ./tests/KynticAI.Scout.IntegrationTests/KynticAI.Scout.IntegrationTests.csproj --filter "FullyQualifiedName~V1RestApiIntegrationTests|FullyQualifiedName~GraphQlAuthorizationIntegrationTests"
dotnet test ./tests/KynticAI.Scout.UnitTests/KynticAI.Scout.UnitTests.csproj --filter "FullyQualifiedName~ConnectorPluginModelTests|FullyQualifiedName~SelectorExecutionEngineTests"

step "Optional PostgreSQL smoke"
if [[ -n "${ConnectionStrings__Scout:-}" && -n "${ConnectionStrings__CustomerOps:-}" ]]; then
  dotnet test ./tests/KynticAI.Scout.IntegrationTests/KynticAI.Scout.IntegrationTests.csproj --filter "FullyQualifiedName~BackendOnlyModeIntegrationTests"
else
  echo "Skipped: PostgreSQL connection strings are not set."
fi

step "Public forbidden-code scan"
if rg -n "using KynticAIScout\.Enterprise|namespace KynticAIScout\.Enterprise|Scout\.Cloud\.Api|StripeSecret|OAuthRefreshToken|BEGIN PRIVATE KEY|service_account|Fortress|pgvector|Rust engine|vector DB|private LLM" src apps packages docs docs-site/src deploy tools; then
  fail "public forbidden-code scan found private implementation or secret markers."
fi

echo "Pilot readiness checks completed."
