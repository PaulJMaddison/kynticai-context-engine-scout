#!/usr/bin/env bash
set -euo pipefail

# tag-release.sh -- Create an annotated git tag for a Scout release and push it.
#
# Usage:
#   ./scripts/tag-release.sh vX.Y.Z [--dry-run]
#
# The version argument must match the pattern vMAJOR.MINOR.PATCH.
# Use --dry-run to preview what would happen without making changes.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

DRY_RUN=false
VERSION=""

for arg in "$@"; do
  case "$arg" in
    --dry-run)
      DRY_RUN=true
      ;;
    -*)
      echo "ERROR: Unknown flag: $arg" >&2
      echo "Usage: $0 vX.Y.Z [--dry-run]" >&2
      exit 1
      ;;
    *)
      if [ -z "$VERSION" ]; then
        VERSION="$arg"
      else
        echo "ERROR: Unexpected argument: $arg" >&2
        echo "Usage: $0 vX.Y.Z [--dry-run]" >&2
        exit 1
      fi
      ;;
  esac
done

if [ -z "$VERSION" ]; then
  echo "ERROR: Version argument is required." >&2
  echo "Usage: $0 vX.Y.Z [--dry-run]" >&2
  exit 1
fi

# Validate version format: must be vMAJOR.MINOR.PATCH
if ! echo "$VERSION" | grep -qE '^v[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "ERROR: Version must match the pattern vX.Y.Z (e.g. v2.8.0)." >&2
  exit 1
fi

echo "KynticAI Scout — Tag Release"
echo "======================================"
echo "Version:  $VERSION"
echo "Repo:     $REPO_ROOT"
echo "Dry run:  $DRY_RUN"
echo ""

# Check that the working tree is clean
if [ -n "$(git status --porcelain)" ]; then
  echo "ERROR: Working tree is not clean. Commit or stash changes before tagging." >&2
  git status --short >&2
  exit 1
fi

echo "Working tree is clean."

# Check that the tag does not already exist
if git rev-parse "$VERSION" >/dev/null 2>&1; then
  echo "ERROR: Tag $VERSION already exists." >&2
  exit 1
fi

echo "Tag $VERSION does not exist yet."

# Run tests
echo ""
echo "Running tests..."
if command -v dotnet >/dev/null 2>&1; then
  DOTNET_CMD="dotnet"
elif [ -x "$REPO_ROOT/.dotnet/dotnet" ]; then
  DOTNET_CMD="$REPO_ROOT/.dotnet/dotnet"
else
  echo "WARNING: dotnet not found. Skipping test run." >&2
  DOTNET_CMD=""
fi

if [ -n "$DOTNET_CMD" ]; then
  if [ -f "$REPO_ROOT/KynticAI.Scout.slnx" ]; then
    echo "Running: $DOTNET_CMD test KynticAI.Scout.slnx --configuration Release"
    if ! $DOTNET_CMD test "$REPO_ROOT/KynticAI.Scout.slnx" --configuration Release; then
      echo "ERROR: Tests failed. Fix test failures before tagging." >&2
      exit 1
    fi
    echo "All tests passed."
  else
    echo "WARNING: KynticAI.Scout.slnx not found. Skipping test run." >&2
  fi
fi

echo ""

if [ "$DRY_RUN" = true ]; then
  echo "[DRY RUN] Would create annotated tag: $VERSION"
  echo "[DRY RUN] Would push tag to origin: git push origin $VERSION"
  echo ""
  echo "Dry run complete. No changes were made."
  exit 0
fi

# Create annotated tag
echo "Creating annotated tag $VERSION..."
git tag -a "$VERSION" -m "Release $VERSION"
echo "Tag $VERSION created."

# Push tag to origin
echo "Pushing tag $VERSION to origin..."
git push origin "$VERSION"
echo "Tag $VERSION pushed to origin."

echo ""
echo "======================================"
echo "Tag $VERSION created and pushed."
echo ""
echo "Next steps:"
echo "  1. If the GitHub Actions release workflow is enabled (not renamed to"
echo "     .disabled), wait for it to complete:"
echo "     https://github.com/PaulJMaddison/scout/actions"
echo "  2. If the release workflow is disabled, create the GitHub Release"
echo "     manually from the tag:"
echo "     gh release create $VERSION --generate-notes"
echo "  3. Verify the GitHub Release was created:"
echo "     https://github.com/PaulJMaddison/scout/releases/tag/$VERSION"
echo "  4. Coordinate private extension package tags if needed:"
echo "     cd <private-extension-repo>"
echo "     git tag -a $VERSION -m \"Release $VERSION\" && git push origin $VERSION"
echo "  5. Coordinate private control-plane package tags if needed:"
echo "     cd <private-control-plane-repo>"
echo "     git tag -a $VERSION -m \"Release $VERSION\" && git push origin $VERSION"
echo "  6. Build and push private Docker images only where applicable."
echo "  7. Complete the post-release checklist in docs/releases/release-process.md."
