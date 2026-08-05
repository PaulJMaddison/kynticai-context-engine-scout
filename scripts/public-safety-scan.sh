#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

# Public-safety scan: fails if any forbidden private-extension, cloud-api,
# secret-marker, key, or private-runtime term appears in tracked public
# directories. Shared by the pilot-readiness gate and CI (ci.yml / release.yml)
# so the two can never drift apart. Scans tracked files only (via git grep),
# which matches what CI checks out.
pattern="using KynticAIScout\.Enterprise|namespace KynticAIScout\.Enterprise|Scout\.Cloud\.Api|StripeSecret|OAuthRefreshToken|BEGIN PRIVATE KEY|service_account|Fortress|pgvector|Rust engine|vector DB|private LLM"
if git grep -nIE "$pattern" -- src apps packages docs docs-site/src deploy tools; then
  echo "public-safety scan found private implementation or secret markers." >&2
  exit 1
fi

echo "Public-safety scan clean."
