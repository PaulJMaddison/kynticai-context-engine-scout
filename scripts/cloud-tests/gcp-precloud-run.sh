#!/usr/bin/env bash
set -euo pipefail

: "${GCP_PROJECT_ID:?Set GCP_PROJECT_ID.}"

GCP_ZONE="${GCP_ZONE:-europe-west2-b}"
SCOUT_VM_NAME="${SCOUT_VM_NAME:-scout-precloud-test}"
SCOUT_BRANCH="${SCOUT_BRANCH:-chatgpt/precloud-static-fixes-20260816}"
SCOUT_REPOSITORY="${SCOUT_REPOSITORY:-https://github.com/PaulJMaddison/kynticai-context-engine-scout.git}"
SCOUT_SCALE_ROWS="${SCOUT_SCALE_ROWS:-100000}"

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
sudo apt-get install -y ca-certificates curl git jq openssl docker.io docker-compose postgresql-client
sudo systemctl enable --now docker

if [[ ! -x "$HOME/.dotnet/dotnet" ]]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --version 10.0.203 --install-dir "$HOME/.dotnet"
fi
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

dotnet --info

rm -rf "$HOME/scout-precloud"
git clone --branch "$SCOUT_BRANCH" --single-branch "$SCOUT_REPOSITORY" "$HOME/scout-precloud"
cd "$HOME/scout-precloud"

TEST_SHA=$(git rev-parse HEAD)
echo "SCOUT_TEST_SHA=$TEST_SHA"

# The repository has no local dotnet-tool manifest. Install the EF CLI explicitly into the
# disposable VM and pin it to the EF Core package version used by the repository.
rm -rf "$HOME/.scout-dotnet-tools"
dotnet tool install --tool-path "$HOME/.scout-dotnet-tools" dotnet-ef --version 10.0.7

# Static/compiled gate. Nothing cloud-specific is allowed to compensate for a failure here.
dotnet restore KynticAI.Scout.slnx
dotnet build KynticAI.Scout.slnx --configuration Release --no-restore -warnaserror
dotnet test KynticAI.Scout.slnx --configuration Release --no-restore --no-build

# EF Core 10 must see the checked-in snapshot as equal to the runtime model.
"$HOME/.scout-dotnet-tools/dotnet-ef" migrations has-pending-model-changes \
  --project src/KynticAI.Scout.Infrastructure/KynticAI.Scout.Infrastructure.csproj \
  --startup-project src/KynticAI.Scout.Api/KynticAI.Scout.Api.csproj

# Start production-like Postgres locally on the disposable VM. No public firewall rule is created.
export POSTGRES_PASSWORD="$(openssl rand -hex 32)"
export AUTH_SIGNING_KEY="$(openssl rand -hex 64)"
export SEED_DEMO_DATA=false
sudo -E docker-compose -f deploy/docker-compose.yml --profile postgres up -d postgres

for i in $(seq 1 60); do
  if sudo -E docker-compose -f deploy/docker-compose.yml --profile postgres exec -T postgres \
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
# during normal startup; there is deliberately no synthetic "migrate" CLI path in Scout.
sudo -E docker-compose -f deploy/docker-compose.yml --profile postgres up -d scout-api-pg
for i in $(seq 1 60); do
  if curl -fsS http://127.0.0.1:8080/health/ready >/tmp/scout-ready.json 2>/dev/null; then
    break
  fi
  sleep 2
  if [[ "$i" == "60" ]]; then
    sudo -E docker-compose -f deploy/docker-compose.yml --profile postgres logs --no-color scout-api-pg >&2
    echo "Scout API did not become ready after applying PostgreSQL migrations." >&2
    exit 11
  fi
done
cat /tmp/scout-ready.json

# The ownership table and expected indexes must exist after the API's normal migration/startup path.
sudo -E docker-compose -f deploy/docker-compose.yml --profile postgres exec -T postgres \
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

# Deterministic ownership/export tests already live in the .NET test suite. Run the focused tests
# again after the real PostgreSQL migration/startup gate so failures are easy to attribute.
dotnet test tests/KynticAI.Scout.UnitTests/KynticAI.Scout.UnitTests.csproj \
  --configuration Release --no-restore --no-build \
  --filter 'FullyQualifiedName~ConnectorCaptureOwnershipTests'

# Store a small, non-customer-data proof bundle locally on the VM for manual collection if wanted.
mkdir -p "$HOME/scout-precloud-proof"
{
  echo "tested_sha=$TEST_SHA"
  echo "branch=$SCOUT_BRANCH"
  echo "scale_rows=$SCOUT_SCALE_ROWS"
  echo "completed_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  dotnet --version | sed 's/^/dotnet=/'
  sudo -E docker-compose -f deploy/docker-compose.yml --profile postgres ps
} > "$HOME/scout-precloud-proof/summary.txt"

# Scale fixture generation/export is intentionally a separate explicit step because connector-specific
# fixture credentials are not committed to the repo. The cloud runbook defines the 100k required and
# 1m optional acceptance criteria and the anti-resurrection/cutover race matrix.

echo "CORE_PRECLOUD_VALIDATION=PASS"
echo "Proof: $HOME/scout-precloud-proof/summary.txt"
EOF
)

SCOUT_BRANCH_Q=$(printf '%q' "${SCOUT_BRANCH}")
SCOUT_REPOSITORY_Q=$(printf '%q' "${SCOUT_REPOSITORY}")
SCOUT_SCALE_ROWS_Q=$(printf '%q' "${SCOUT_SCALE_ROWS}")

gcloud compute ssh "${SCOUT_VM_NAME}" \
  --zone="${GCP_ZONE}" \
  --command="export SCOUT_BRANCH=${SCOUT_BRANCH_Q} SCOUT_REPOSITORY=${SCOUT_REPOSITORY_Q} SCOUT_SCALE_ROWS=${SCOUT_SCALE_ROWS_Q}; bash -s" \
  <<<"${REMOTE_SCRIPT}"
