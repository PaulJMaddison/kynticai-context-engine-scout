#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

fail() { echo "Pilot readiness failed: $1" >&2; exit 1; }
step() { echo "==> $1"; }

step "GitHub Actions workflow safety"
if [[ ! -d .github/workflows ]] || ! find .github/workflows -maxdepth 1 -type f \( -name '*.yml' -o -name '*.yaml' \) | grep -q .; then
  fail ".github/workflows must contain active workflow files for CI."
fi

workflow_forbidden=(
  '\$\{\{\s*secrets\.'
  '-----BEGIN'
  'AKIA[0-9A-Z]{16}'
  'ghp_[A-Za-z0-9]{20,}'
  'github_pat_[A-Za-z0-9_]{20,}'
  'xox[baprs]-'
  'git@github\.com'
  'repository_dispatch'
)
while IFS= read -r workflow; do
  for pattern in "${workflow_forbidden[@]}"; do
    if grep -qE "$pattern" "$workflow"; then
      fail "workflow $workflow contains forbidden content matching: $pattern"
    fi
  done
  if grep -qE '\b(playwright|test:e2e)\b' "$workflow" && ! grep -q 'KYNTIC_RUN_BROWSER_TESTS' "$workflow"; then
    fail "workflow $workflow has browser steps without the KYNTIC_RUN_BROWSER_TESTS opt-in."
  fi
  if grep -qE '\bdocker\b' "$workflow" && ! grep -q 'KYNTIC_RUN_EXTERNAL_DOTNET_TESTS' "$workflow"; then
    fail "workflow $workflow has container steps without the KYNTIC_RUN_EXTERNAL_DOTNET_TESTS opt-in."
  fi
done < <(find .github/workflows -maxdepth 1 -type f \( -name '*.yml' -o -name '*.yaml' \) | sort)

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
if ! bash scripts/public-safety-scan.sh; then
  fail "public forbidden-code scan found private implementation or secret markers."
fi

echo "Pilot readiness checks completed."
