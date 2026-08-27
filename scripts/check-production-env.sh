#!/usr/bin/env bash
set -euo pipefail

ENV_FILE="${1:-.env}"

get_setting() {
  local name="$1"
  local value="${!name:-}"
  if [ -n "$value" ]; then
    printf '%s' "$value"
    return
  fi

  if [ -f "$ENV_FILE" ]; then
    grep -E "^${name}=" "$ENV_FILE" | tail -n 1 | cut -d '=' -f 2- | sed -e 's/^"//' -e 's/"$//' -e "s/^'//" -e "s/'$//"
  fi
}

is_true() {
  printf '%s' "$1" | grep -Eiq '^(1|true|yes|on)$'
}

is_placeholder() {
  local value="$1"
  [ -z "$value" ] && return 0
  printf '%s' "$value" | grep -Eiq '(replace|change|placeholder|example|demo|development-only|password|secret|localhost;|Data Source=\.demo-data)'
}

failures=()

platform_mode="$(get_setting Platform__Mode || true)"
database_provider="$(get_setting Database__Provider || true)"
context_connection="$(get_setting ConnectionStrings__Scout || true)"
signing_key="$(get_setting Auth__SigningKey || true)"
demo_fallback="$(get_setting VITE_DEMO_FALLBACK || true)"
seed_demo_data="$(get_setting Bootstrap__SeedDemoData || true)"
demo_experience="$(get_setting FeatureFlags__DemoExperience || true)"
key_ring_path="$(get_setting DataProtection__KeyRingPath || true)"
persistent_keys="$(get_setting DataProtection__RequirePersistentKeys || true)"

[ "$demo_fallback" = "false" ] || failures+=("VITE_DEMO_FALLBACK must be false for production-style builds.")
case "$platform_mode" in
  SelfHosted|ManagedDataPlane|BackendOnly|SaaS) ;;
  *) failures+=("Platform__Mode must be SelfHosted or ManagedDataPlane for new production deployments (BackendOnly/SaaS are legacy compatibility values).") ;;
esac
[ "$database_provider" = "Postgres" ] || failures+=("Database__Provider must be Postgres for production-style deployment.")
[ "$persistent_keys" = "true" ] || failures+=("DataProtection__RequirePersistentKeys must be true.")

if is_placeholder "$signing_key" || [ "${#signing_key}" -lt 48 ]; then
  failures+=("Auth__SigningKey must be a non-placeholder value of at least 48 characters.")
fi

if printf '%s' "$context_connection" | grep -Eiq 'Data Source=|Sqlite|\.db|\.sqlite'; then
  failures+=("SQLite/local database connection strings are not acceptable for production-style deployment.")
fi

[ -n "$context_connection" ] || failures+=("ConnectionStrings__Scout must be configured.")
printf '%s' "$context_connection" | grep -Eq 'Host=|Server=|Database=' || failures+=("ConnectionStrings__Scout must look like a PostgreSQL connection string.")

if [ "${ALLOW_DEMO_DATA:-false}" != "true" ]; then
  if is_true "$seed_demo_data"; then
    failures+=("Bootstrap__SeedDemoData must be false unless ALLOW_DEMO_DATA=true is explicitly set for rehearsal.")
  fi
  if is_true "$demo_experience"; then
    failures+=("FeatureFlags__DemoExperience must be false for customer/prod-style deployments.")
  fi
fi

if [ -z "$key_ring_path" ] || printf '%s' "$key_ring_path" | grep -Eiq '\.demo-data|temp|tmp'; then
  failures+=("DataProtection__KeyRingPath must be a persistent mounted path.")
fi

echo "Public data-plane production environment preflight"
echo "Environment file: $ENV_FILE"
echo "Platform__Mode: $platform_mode"
echo "Database__Provider: $database_provider"
echo "VITE_DEMO_FALLBACK: $demo_fallback"
echo "Bootstrap__SeedDemoData: $seed_demo_data"
echo "FeatureFlags__DemoExperience: $demo_experience"
echo "DataProtection__KeyRingPath configured: $([ -n "$key_ring_path" ] && echo true || echo false)"
echo "Scout connection string configured: $([ -n "$context_connection" ] && echo true || echo false)"

if [ "${#failures[@]}" -gt 0 ]; then
  echo
  echo "Preflight failed:"
  for failure in "${failures[@]}"; do
    echo "- $failure"
  done
  exit 1
fi

echo "Preflight passed. No secrets were printed."
