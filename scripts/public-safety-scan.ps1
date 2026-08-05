# Public-safety scan: fails if any forbidden private-extension, cloud-api,
# secret-marker, key, or private-runtime term appears in tracked public
# directories. Shared by the pilot-readiness gate and CI so the two can never
# drift apart. Scans tracked files only (via git grep), which matches what CI
# checks out.
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$pattern = "using KynticAIScout\.Enterprise|namespace KynticAIScout\.Enterprise|Scout\.Cloud\.Api|StripeSecret|OAuthRefreshToken|BEGIN PRIVATE KEY|service_account|Fortress|pgvector|Rust engine|vector DB|private LLM"
$forbidden = git grep -nIE -- "$pattern" -- src apps packages docs docs-site/src deploy tools
if ($LASTEXITCODE -eq 0) {
    Write-Host $forbidden
    Write-Host "public-safety scan found private implementation or secret markers."
    exit 1
}

Write-Host "Public-safety scan clean."
exit 0
