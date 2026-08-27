#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5198"
RUN_DOCKER="false"
RUN_MIGRATIONS="false"
SKIP_BUILD="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url) BASE_URL="$2"; shift 2 ;;
    --run-docker) RUN_DOCKER="true"; shift ;;
    --run-migrations) RUN_MIGRATIONS="true"; shift ;;
    --skip-build) SKIP_BUILD="true"; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

step() {
  echo
  echo "== $1 =="
}

require_opt_in() {
  local name="$1" purpose="$2"
  local value="${!name:-}"
  if [[ "$value" != "1" && "${value,,}" != "true" ]]; then
    echo "$purpose is opt-in. Set $name=1 to run this proof path." >&2
    exit 1
  fi
}

assert_setting() {
  local name="$1"
  local expected="$2"
  local actual="$3"
  if [[ "$actual" != "$expected" ]]; then
    echo "$name must be '$expected'. Current value: '$actual'." >&2
    exit 1
  fi
}

assert_not_placeholder() {
  local name="$1"
  local value="${2:-}"
  local min_length="$3"
  if [[ -z "$value" || ${#value} -lt "$min_length" || "$value" =~ development-only|change|replace|password|secret ]]; then
    echo "$name must be supplied from a secret store and be at least $min_length characters." >&2
    exit 1
  fi
}

step "Production-style configuration checks"
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
Platform__Mode="${Platform__Mode:-SelfHosted}"
Database__Provider="${Database__Provider:-Postgres}"
Bootstrap__SeedDemoData="${Bootstrap__SeedDemoData:-false}"
VITE_DEMO_FALLBACK="${VITE_DEMO_FALLBACK:-false}"
DataProtection__RequirePersistentKeys="${DataProtection__RequirePersistentKeys:-true}"

assert_setting "ASPNETCORE_ENVIRONMENT" "Production" "$ASPNETCORE_ENVIRONMENT"
if [[ "$Platform__Mode" != "SelfHosted" && "$Platform__Mode" != "ManagedDataPlane" && "$Platform__Mode" != "SaaS" && "$Platform__Mode" != "BackendOnly" ]]; then
  echo "Platform__Mode must be SelfHosted or ManagedDataPlane (BackendOnly/SaaS remain compatibility aliases). Current value: '$Platform__Mode'." >&2
  exit 1
fi
assert_setting "Database__Provider" "Postgres" "$Database__Provider"
assert_setting "Bootstrap__SeedDemoData" "false" "$Bootstrap__SeedDemoData"
assert_setting "VITE_DEMO_FALLBACK" "false" "$VITE_DEMO_FALLBACK"
assert_setting "DataProtection__RequirePersistentKeys" "true" "$DataProtection__RequirePersistentKeys"
assert_not_placeholder "Auth__SigningKey" "${Auth__SigningKey:-}" 48
if [[ -z "${ConnectionStrings__Scout:-}" ]]; then
  echo "ConnectionStrings__Scout must be set to the single Scout PostgreSQL connection string." >&2
  exit 1
fi
echo "Configuration checks passed."

resolve_dotnet() {
  if [[ -x "./.dotnet/dotnet" ]]; then
    echo "./.dotnet/dotnet"
    return
  fi
  if [[ -x "./.dotnet/dotnet.exe" ]]; then
    echo "./.dotnet/dotnet.exe"
    return
  fi
  command -v dotnet
}

step ".NET build check"
DOTNET="$(resolve_dotnet)" || { echo ".NET SDK is not available." >&2; exit 1; }
"$DOTNET" --info | sed -n '1,20p'
if [[ "$SKIP_BUILD" != "true" ]]; then
  "$DOTNET" build ./KynticAI.Scout.slnx
fi

step "Migration path"
echo "dotnet run --project ./src/KynticAI.Scout.Api/KynticAI.Scout.Api.csproj -- migrate"
if [[ "$RUN_MIGRATIONS" == "true" ]]; then
  "$DOTNET" run --project ./src/KynticAI.Scout.Api/KynticAI.Scout.Api.csproj -- migrate
else
  echo "Not running migrations because --run-migrations was not supplied."
fi

step "Backup and restore commands"
echo "pg_dump --format=custom --file ./backup/scout_context_db.dump scout_context_db"
echo "createdb scout_context_restore_check"
echo "pg_restore --clean --if-exists --dbname scout_context_restore_check ./backup/scout_context_db.dump"

step "Docker/PostgreSQL rehearsal"
if [[ "$RUN_DOCKER" == "true" ]]; then
  require_opt_in "KYNTIC_RUN_EXTERNAL_DOTNET_TESTS" "Docker/PostgreSQL rehearsal"
  command -v docker >/dev/null || { echo "Docker is not available." >&2; exit 1; }
  docker compose up -d postgres
  docker compose ps postgres
else
  echo "Not starting Docker because --run-docker was not supplied."
  echo "To run locally: docker compose up -d postgres"
fi

step "Endpoint smoke checks"
for path in /health/live /health/ready /health /api/v1/health; do
  if command -v curl >/dev/null; then
    curl -fsS "$BASE_URL$path" || echo "Could not reach $BASE_URL$path; start the API and rerun smoke checks."
    echo
  fi
done

echo "GraphQL endpoint: $BASE_URL/graphql"
echo "REST endpoints: $BASE_URL/api/rest and $BASE_URL/api/v1"
echo "Machine auth endpoint: $BASE_URL/api/auth/token"
echo "Logs: inspect host/container stdout and configured OpenTelemetry collector."
