#!/usr/bin/env bash
set -euo pipefail

: "${GCP_PROJECT_ID:?Set GCP_PROJECT_ID.}"

GCP_ZONE="${GCP_ZONE:-europe-west2-b}"
SCOUT_VM_NAME="${SCOUT_VM_NAME:-scout-precloud-test}"
SCOUT_BRANCH="${SCOUT_BRANCH:-main}"
SCOUT_EXPECTED_SHA="${SCOUT_EXPECTED_SHA:-}"
SCOUT_REPOSITORY="${SCOUT_REPOSITORY:-https://github.com/PaulJMaddison/kynticai-context-engine-scout.git}"
SCOUT_SCALE_ROWS="${SCOUT_SCALE_ROWS:-100000}"
NODE_VERSION="${NODE_VERSION:-24.19.0}"

case "${SCOUT_SCALE_ROWS}" in
  100000|1000000) ;;
  *)
    echo "SCOUT_SCALE_ROWS must be 100000 or 1000000." >&2
    exit 2
    ;;
esac

gcloud config set project "${GCP_PROJECT_ID}" >/dev/null

REMOTE_SCRIPT=$(cat <<'EOF'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

sudo apt-get update
sudo apt-get install -y ca-certificates curl git jq openssl xz-utils docker.io docker-compose postgresql-client
sudo systemctl enable --now docker

if [[ ! -x "$HOME/.dotnet/dotnet" ]]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --version 10.0.203 --install-dir "$HOME/.dotnet"
fi
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

dotnet --info

NODE_HOME="$HOME/.node-v${NODE_VERSION}"
if [[ ! -x "$NODE_HOME/bin/node" ]]; then
  rm -rf "$NODE_HOME"
  mkdir -p "$NODE_HOME"
  curl -fsSLO "https://nodejs.org/download/release/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz"
  curl -fsSLO "https://nodejs.org/download/release/v${NODE_VERSION}/SHASUMS256.txt"
  grep " node-v${NODE_VERSION}-linux-x64.tar.xz$" SHASUMS256.txt | sha256sum -c -
  tar -xJf "node-v${NODE_VERSION}-linux-x64.tar.xz" --strip-components=1 -C "$NODE_HOME"
  rm -f "node-v${NODE_VERSION}-linux-x64.tar.xz" SHASUMS256.txt
fi
export PATH="$NODE_HOME/bin:$PATH"
node --version
npm --version

rm -rf "$HOME/scout-precloud"
git clone --branch "$SCOUT_BRANCH" --single-branch "$SCOUT_REPOSITORY" "$HOME/scout-precloud"
cd "$HOME/scout-precloud"

TEST_SHA=$(git rev-parse HEAD)
echo "SCOUT_TEST_SHA=$TEST_SHA"
if [[ -n "$SCOUT_EXPECTED_SHA" && "$TEST_SHA" != "$SCOUT_EXPECTED_SHA" ]]; then
  echo "Expected Scout SHA '$SCOUT_EXPECTED_SHA' but cloned '$TEST_SHA'. Refusing to validate the wrong revision." >&2
  exit 3
fi

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Fresh validation checkout is unexpectedly dirty before tests." >&2
  git status --short >&2
  exit 4
fi

# Debian 12's packaged docker-compose is Compose v1. The committed compose file intentionally
# uses the v2 top-level `name:` key for normal local development, so make a disposable cloud-only
# copy without that key while keeping it beside the original file so relative build contexts and
# volume paths remain identical.
COMPOSE_FILE="deploy/docker-compose.cloud.yml"
sed '/^name: scout$/d' deploy/docker-compose.yml > "${COMPOSE_FILE}"

# The repository has no local dotnet-tool manifest. Install the EF CLI explicitly into the
# disposable VM and pin it to the EF Core package version used by the repository.
rm -rf "$HOME/.scout-dotnet-tools"
dotnet tool install --tool-path "$HOME/.scout-dotnet-tools" dotnet-ef --version 10.0.7

# .NET compile/static/test gate.
dotnet restore KynticAI.Scout.slnx
dotnet format KynticAI.Scout.slnx --verify-no-changes --no-restore
dotnet build KynticAI.Scout.slnx --configuration Release --no-restore -warnaserror
dotnet test KynticAI.Scout.slnx --configuration Release --no-restore --no-build

# Run every committed Node/TypeScript product surface, not just the web app. Packages with a lock
# use npm ci. The generic discovery-agent lock was deliberately removed when the private buyer MCP
# wrapper was separated from Scout; until a fresh lock is committed it installs without writing a
# replacement so cloud validation does not dirty the checkout.
run_node_package() {
  local package_dir="$1"
  local run_lint="${2:-false}"
  local run_pack="${3:-false}"

  echo "=== Node validation: ${package_dir} ==="
  pushd "$package_dir" >/dev/null
  if [[ -f package-lock.json ]]; then
    npm ci
  else
    npm install --package-lock=false
  fi
  npm run build --if-present
  npm test --if-present
  if [[ "$run_lint" == "true" ]]; then
    npm run lint
  fi
  if [[ "$run_pack" == "true" ]]; then
    npm run pack:dry-run
  fi
  popd >/dev/null
}

run_node_package packages/typescript/scout-connector-validator
run_node_package packages/typescript/scout-metadata-audit
run_node_package packages/typescript/scout-connector-test-harness
run_node_package packages/typescript/scout-contract-parity
run_node_package packages/typescript/scout-discovery-mcp
run_node_package packages/typescript/scout-sdk false true
run_node_package packages/typescript/n8n-node false true
run_node_package packages/typescript/scout-n8n-node false true
run_node_package apps/discovery-agent
run_node_package apps/web true false

# Smoke the public generic discovery agent against this exact checkout. This is intentionally the
# codebase-audit agent, not the private KynticAI Discovery MCP buyer workflow that lives in Fortress.
pushd apps/discovery-agent >/dev/null
node dist/index.js --path ../.. --tier 1 >/tmp/scout-discovery-agent-tier1.json
jq -e . /tmp/scout-discovery-agent-tier1.json >/dev/null
popd >/dev/null

# EF Core 10 must see the checked-in snapshot as equal to the runtime model.
"$HOME/.scout-dotnet-tools/dotnet-ef" migrations has-pending-model-changes \
  --project src/KynticAI.Scout.Infrastructure/KynticAI.Scout.Infrastructure.csproj \
  --startup-project src/KynticAI.Scout.Api/KynticAI.Scout.Api.csproj

# Start production-like Postgres locally on the disposable VM. No public firewall rule is created.
export POSTGRES_PASSWORD="$(openssl rand -hex 32)"
export AUTH_SIGNING_KEY="$(openssl rand -hex 64)"
export SEED_DEMO_DATA=false
sudo -E docker-compose -f "${COMPOSE_FILE}" --profile postgres up -d postgres

for i in $(seq 1 60); do
  if sudo -E docker-compose -f "${COMPOSE_FILE}" --profile postgres exec -T postgres \
      pg_isready -U postgres -d postgres >/dev/null 2>&1; then
    break
  fi
  sleep 2
  if [[ "$i" == "60" ]]; then
    echo "PostgreSQL did not become ready." >&2
    exit 10
  fi
done

# Bring the real API up. PostgreSQL migrations are applied by ApplicationBootstrapper/MigrateAsync
# during normal startup; the dedicated migrate/bootstrap CLI remains available for operators, but
# this proof deliberately exercises the normal production startup path.
sudo -E docker-compose -f "${COMPOSE_FILE}" --profile postgres up -d scout-api-pg
for i in $(seq 1 60); do
  if curl -fsS http://127.0.0.1:8080/health/ready >/tmp/scout-ready.json 2>/dev/null; then
    break
  fi
  sleep 2
  if [[ "$i" == "60" ]]; then
    sudo -E docker-compose -f "${COMPOSE_FILE}" --profile postgres logs --no-color scout-api-pg >&2
    echo "Scout API did not become ready after applying PostgreSQL migrations." >&2
    exit 11
  fi
done
cat /tmp/scout-ready.json

# The ownership table and expected indexes must exist after the API's normal migration/startup path.
sudo -E docker-compose -f "${COMPOSE_FILE}" --profile postgres exec -T postgres \
  psql -v ON_ERROR_STOP=1 -U postgres -d scout_context_db <<'SQL'
DO $$
BEGIN
  IF to_regclass('public.connector_capture_ownership') IS NULL THEN
    RAISE EXCEPTION 'connector_capture_ownership migration did not create the table';
  END IF;
END $$;

SELECT indexname
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename = 'connector_capture_ownership'
ORDER BY indexname;
SQL

# Re-run the most safety-critical continuity and new failure-path suites after the real Postgres
# migration/startup gate so any provider-sensitive regression is easy to attribute.
dotnet test tests/KynticAI.Scout.UnitTests/KynticAI.Scout.UnitTests.csproj \
  --configuration Release --no-restore --no-build \
  --filter 'FullyQualifiedName~ConnectorCaptureOwnershipTests|FullyQualifiedName~ContinuityCaptureProofTests|FullyQualifiedName~ContextRecomputeQueueTests|FullyQualifiedName~PasswordHashingServiceTests|FullyQualifiedName~CorsOriginValidatorTests'

dotnet test tests/KynticAI.Scout.IntegrationTests/KynticAI.Scout.IntegrationTests.csproj \
  --configuration Release --no-restore --no-build \
  --filter 'FullyQualifiedName~BackendOnlyModeIntegrationTests'

# The validation process may create build output and the disposable Compose compatibility file,
# but it must not modify tracked source/configuration files.
if ! git diff --exit-code -- . ':(exclude)deploy/docker-compose.cloud.yml'; then
  echo "Validation modified tracked repository files." >&2
  exit 12
fi

mkdir -p "$HOME/scout-precloud-proof"
{
  echo "tested_sha=$TEST_SHA"
  echo "branch=$SCOUT_BRANCH"
  echo "scale_rows=$SCOUT_SCALE_ROWS"
  echo "completed_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "dotnet=$(dotnet --version)"
  echo "node=$(node --version)"
  echo "npm=$(npm --version)"
  if [[ -f apps/discovery-agent/package-lock.json ]]; then
    echo "discovery_agent_lock=present"
  else
    echo "discovery_agent_lock=absent"
  fi
  sudo -E docker-compose -f "${COMPOSE_FILE}" --profile postgres ps
} > "$HOME/scout-precloud-proof/summary.txt"

# Connector-specific 100k/1m source fixtures remain an explicit follow-on because customer/source
# credentials and large fixture data are intentionally not committed to this public repository.
echo "REPO_WIDE_PRECLOUD_VALIDATION=PASS"
echo "Proof: $HOME/scout-precloud-proof/summary.txt"
EOF
)

SCOUT_BRANCH_Q=$(printf '%q' "${SCOUT_BRANCH}")
SCOUT_EXPECTED_SHA_Q=$(printf '%q' "${SCOUT_EXPECTED_SHA}")
SCOUT_REPOSITORY_Q=$(printf '%q' "${SCOUT_REPOSITORY}")
SCOUT_SCALE_ROWS_Q=$(printf '%q' "${SCOUT_SCALE_ROWS}")
NODE_VERSION_Q=$(printf '%q' "${NODE_VERSION}")

gcloud compute ssh "${SCOUT_VM_NAME}" \
  --zone="${GCP_ZONE}" \
  --command="export SCOUT_BRANCH=${SCOUT_BRANCH_Q} SCOUT_EXPECTED_SHA=${SCOUT_EXPECTED_SHA_Q} SCOUT_REPOSITORY=${SCOUT_REPOSITORY_Q} SCOUT_SCALE_ROWS=${SCOUT_SCALE_ROWS_Q} NODE_VERSION=${NODE_VERSION_Q}; bash -s" \
  <<<"${REMOTE_SCRIPT}"
