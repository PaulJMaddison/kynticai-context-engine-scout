# Release Process

This document describes the public KynticAI Scout release process and the coordination points for any private packages that are released alongside it.

| Area | Visibility | Purpose |
|---|---|---|
| [scout](https://github.com/PaulJMaddison/kynticai-context-engine-scout) | Public | Open-source core: domain, APIs, SDKs, React frontend |
| Private extensions | Private | Enterprise extensions: connectors, governance, identity, deployment |
| Private control plane | Private | Hosted control plane: accounts, licensing, billing, portal |

Private package releases should be coordinated deliberately when they depend on the same public Scout version.

---

## Version Numbering

Scout follows [Semantic Versioning 2.0.0](https://semver.org/):

```
vMAJOR.MINOR.PATCH
```

| Bump | When |
|---|---|
| **MAJOR** | Breaking changes to the public API contract, GraphQL schema, REST endpoints, SDK interfaces, or data model migrations that are not backwards-compatible. |
| **MINOR** | New features, new endpoints, new connector families, new SDK capabilities, or backwards-compatible enhancements. |
| **PATCH** | Bug fixes, security patches, documentation corrections, or performance improvements with no API surface change. |

### Rules

1. The version string always takes the form `vX.Y.Z` (with the `v` prefix) for git tags and GitHub Releases.
2. Internal file references (`.csproj`, `package.json`, `Chart.yaml`) use `X.Y.Z` without the `v` prefix.
3. Pre-release versions (e.g. `v2.8.0-rc.1`) may be used for release candidates but are not covered by the standard release flow below.
4. Private packages may align to the public version when they are released as part of the same customer delivery.

---

## Cross-Repo Versioning

The public repository may be released on its own or as part of a coordinated private delivery:

- **Public version**: the public repo carries a `vX.Y.Z` tag for every public release.
- **Open-source first**: tag the public repo first so the GitHub Release workflow runs and creates the release artefact before any private package alignment.
- **No accidental private claims**: public release notes must not claim private connector, hosted control-plane, or customer-production capabilities unless those are explicitly public.

### Where version numbers live

| Area | Files |
|---|---|
| Open-source | `Directory.Build.props` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`), `apps/web/package.json`, `packages/typescript/scout-sdk/package.json` |
| Private extensions | Private package version files |
| Private control plane | Private package version files |

Use `scripts/bump-version.sh` to update all version references in a given repo.

---

## Pre-Release Checklist

Before starting the release process, verify **every** item:

- [ ] All tests pass in the open-source repo
- [ ] Any coordinated private package tests pass in their private repositories
- [ ] Run `scripts/check-release-alignment.sh` in the open-source repo -- no warnings
- [ ] No secret leaks: review `git diff` for API keys, signing keys, connection strings, or customer data
- [ ] Version numbers updated in all relevant public files
- [ ] `CHANGELOG.md` and `docs/releases/CHANGELOG.md` updated in the open-source repo
- [ ] Any private package artefacts build successfully where a coordinated private delivery is planned
- [ ] No uncommitted changes in the public repo (`git status` is clean)
- [ ] Public branch is up to date with `main`

---

## Release Steps

Follow these steps **in order**. Do not skip or reorder.

### 1. Create release branches

In the public repo:

```bash
git checkout main
git pull origin main
git checkout -b release/vX.Y.Z
```

### 2. Update version numbers

In the public repo, run the version bump script:

```bash
./scripts/bump-version.sh X.Y.Z
```

Coordinate any private package version updates in private working notes, not in this public release document.

### 3. Update changelogs

Update the following files with the new version entry:

- **Open-source**: `CHANGELOG.md` (root) and `docs/releases/CHANGELOG.md`
- **Private packages**: update private changelogs where a coordinated private delivery is planned

Follow the [Keep a Changelog](https://keepachangelog.com/) format with categories: Added, Changed, Fixed, Removed, Security, Breaking Changes.

### 4. Run the full test suite

```bash
dotnet test KynticAI.Scout.slnx --configuration Release
```

All tests must pass. Do not proceed if any test fails.

### 5. Commit and merge release branches

```bash
git add -A
git commit -m "Release vX.Y.Z"
git checkout main
git merge release/vX.Y.Z --no-ff -m "Merge release/vX.Y.Z"
git push origin main
```

Repeat private package release steps only in the relevant private repositories.

### 6. Tag the open-source repo first

```bash
cd /path/to/scout
./scripts/tag-release.sh vX.Y.Z
```

This creates an annotated tag and pushes it to origin. The GitHub Actions `release.yml` workflow will automatically:
- Build and test the solution
- Create a GitHub Release with auto-generated release notes

If the GitHub Actions workflows are disabled (renamed to `.disabled` while the account billing lock is unresolved), create the GitHub Release manually from the tag instead:

```bash
gh release create vX.Y.Z --generate-notes
```

**Wait** for the GitHub Release to appear before proceeding.

### 7. Coordinate private extension package tags

```bash
cd <private-extension-repo>
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin vX.Y.Z
```

### 8. Coordinate private control-plane package tags

```bash
cd <private-control-plane-repo>
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin vX.Y.Z
```

### 9. Verify the GitHub Release (open-source)

- Navigate to [GitHub Releases](https://github.com/PaulJMaddison/kynticai-context-engine-scout/releases)
- Confirm the release was created by the workflow
- Review the auto-generated release notes
- Edit the release notes if needed to add cross-repo context

### 10. Build and push private Docker images where applicable

```bash
docker build -t <private-image>:vX.Y.Z .
docker tag <private-image>:vX.Y.Z <registry>/<private-image>:vX.Y.Z
docker tag <private-image>:vX.Y.Z <registry>/<private-image>:latest
docker push <registry>/<private-image>:vX.Y.Z
docker push <registry>/<private-image>:latest
```

### 11. Verify packages are accessible

- [ ] GitHub Release page shows the correct tag and release notes
- [ ] Private Docker images are pullable where applicable
- [ ] NuGet packages (if published) are available
- [ ] npm packages (if published) are available

---

## Post-Release Checklist

- [ ] GitHub Releases created with notes for open-source repo
- [ ] Private Docker images tagged and pullable where applicable
- [ ] Marketing site version references updated (README badges, landing page)
- [ ] `docs/roadmap.md` updated if the release closes planned milestones
- [ ] Announce the release internally and to any active pilot customers
- [ ] Build and verify the production web app if frontend changes were included:
  ```bash
  cd apps/web && npm run build
  ```

---

## Hotfix Process

Hotfixes are for critical bug fixes or security patches that cannot wait for the next scheduled release.

### Steps

1. **Branch from the release tag**, not from `main`:
   ```bash
   git checkout vX.Y.Z
   git checkout -b hotfix/vX.Y.(Z+1)
   ```

2. **Apply the fix** with minimal changes. Do not bundle unrelated features.

3. **Bump the patch version** in all three repos:
   ```bash
   ./scripts/bump-version.sh X.Y.(Z+1)
   ```

4. **Update changelogs** with the hotfix entry.

5. **Run the full test suite** in all three repos.

6. **Merge to main** in all three repos:
   ```bash
   git checkout main
   git merge hotfix/vX.Y.(Z+1) --no-ff
   git push origin main
   ```

7. **Tag and release** following the standard release steps (6-11 above).

8. **Cherry-pick to any active release branches** if applicable.

### Hotfix Rules

- Hotfixes always increment the **patch** version.
- The fix must be applied to all three repos, even if only one repo contains the bug, to keep versions aligned.
- If the other repos have no code change, they still get a version bump and changelog entry noting the coordinated release.

---

## Dry Run

Both release scripts support a `--dry-run` flag that previews changes without modifying anything:

```bash
./scripts/tag-release.sh v2.8.0 --dry-run
./scripts/bump-version.sh 2.8.0 --dry-run
```

Always do a dry run first when preparing a release.

---

## Rollback

If a release must be reverted:

1. **Delete the git tag** locally and remotely:
   ```bash
   git tag -d vX.Y.Z
   git push origin :refs/tags/vX.Y.Z
   ```

2. **Delete the GitHub Release** from the Releases page.

3. **Revert the merge commit** on `main`:
   ```bash
   git revert -m 1 <merge-commit-sha>
   git push origin main
   ```

4. **Remove Docker images** from the registry if they were pushed.

5. Repeat for all three repos.

Rollbacks should be rare. Prefer a hotfix release over a rollback when possible.
