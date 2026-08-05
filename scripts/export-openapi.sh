#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# export-openapi.sh — Start the Scout API, download the OpenAPI spec, and stop.
#
# Usage:
#   sh ./scripts/export-openapi.sh
#
# Output:
#   docs/api/openapi.json
# ---------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
API_PROJECT="$REPO_ROOT/src/KynticAI.Scout.Api"
OUTPUT_DIR="$REPO_ROOT/docs/api"
OUTPUT_FILE="$OUTPUT_DIR/openapi.json"
API_URL="http://127.0.0.1:5198"
SWAGGER_URL="$API_URL/swagger/v1/swagger.json"
HEALTH_URL="$API_URL/health/live"
MAX_WAIT=30

# Resolve the dotnet binary (prefer repo-local install).
if [ -x "$REPO_ROOT/.dotnet/dotnet" ]; then
  DOTNET="$REPO_ROOT/.dotnet/dotnet"
else
  DOTNET="dotnet"
fi

cleanup() {
  if [ -n "${API_PID:-}" ] && kill -0 "$API_PID" 2>/dev/null; then
    echo "Stopping API (PID $API_PID)..."
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "Starting Scout API in Development mode..."
ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://127.0.0.1:5198" \
  Platform__EnableOpenApi=true \
  Platform__EnableRest=true \
  "$DOTNET" run --project "$API_PROJECT" --no-launch-profile &
API_PID=$!

echo "Waiting for API health ($HEALTH_URL)..."
elapsed=0
while [ "$elapsed" -lt "$MAX_WAIT" ]; do
  if curl -sf "$HEALTH_URL" >/dev/null 2>&1; then
    echo "API is healthy."
    break
  fi
  sleep 1
  elapsed=$((elapsed + 1))
done

if [ "$elapsed" -ge "$MAX_WAIT" ]; then
  echo "ERROR: API did not become healthy within ${MAX_WAIT}s." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"
echo "Downloading OpenAPI spec from $SWAGGER_URL..."
curl -sf "$SWAGGER_URL" | python3 -m json.tool > "$OUTPUT_FILE"

echo "Saved to $OUTPUT_FILE ($(wc -c < "$OUTPUT_FILE") bytes)."
