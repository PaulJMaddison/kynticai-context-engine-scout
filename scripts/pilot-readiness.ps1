param(
    [switch]$ProductionMode,
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

function Step($name, [scriptblock]$body) {
    Write-Host "==> $name"
    & $body
}

function Fail($message) {
    throw "Pilot readiness failed: $message"
}

Step "No GitHub Actions workflows" {
    if ((Test-Path ".github/workflows") -and (Get-ChildItem ".github/workflows\*" -File -Force -Include "*.yml","*.yaml" | Select-Object -First 1)) {
        Fail ".github/workflows must not contain active workflow files in the public repo."
    }
}

Step "Tracked runtime artefact scan" {
    $matches = git ls-files | Select-String -Pattern '(^|/)(\.env(\.local)?|.*\.(db|sqlite|sqlite3|log|pem|key|pfx|p12|crt|cer)|.*\.lic|.*\.licence\.json|node_modules|bin/|obj/|dist/|support-bundle)' -CaseSensitive:$false
    $unsafe = $matches | Where-Object { $_ -notmatch '\.env\.example$' -and $_ -notmatch 'docs/' -and $_ -notmatch 'LICENSE' }
    if ($unsafe) { $unsafe | ForEach-Object { Write-Host $_ }; Fail "tracked runtime artefacts or secrets were found." }
}

Step "Production example toggles" {
    $envFiles = @(".env.example", "apps/web/.env.example")
    foreach ($file in $envFiles) {
        if (Test-Path $file) {
            $text = Get-Content $file -Raw
            if ($text -notmatch 'VITE_DEMO_FALLBACK=false') { Fail "$file must set VITE_DEMO_FALLBACK=false for production examples." }
        }
    }
    $apiProduction = Get-Content "src/KynticAI.Scout.Api/appsettings.Production.json" -Raw
    if ($apiProduction -notmatch '"SeedDemoData"\s*:\s*false') { Fail "Production appsettings must keep Bootstrap:SeedDemoData=false." }
}

Step "Production PostgreSQL configuration" {
    if ($ProductionMode) {
        if (-not $env:ConnectionStrings__Scout -or -not $env:ConnectionStrings__CustomerOps) {
            Fail "Production mode requires ConnectionStrings__Scout and ConnectionStrings__CustomerOps."
        }
    }
}

if (-not $SkipBuild) {
    Step "Backend build" { dotnet build .\KynticAI.Scout.slnx }
}

if (-not $SkipTests) {
    Step "Focused backend tests" {
        dotnet test .\tests\KynticAI.Scout.IntegrationTests\KynticAI.Scout.IntegrationTests.csproj --filter "FullyQualifiedName~V1RestApiIntegrationTests|FullyQualifiedName~GraphQlAuthorizationIntegrationTests"
        dotnet test .\tests\KynticAI.Scout.UnitTests\KynticAI.Scout.UnitTests.csproj --filter "FullyQualifiedName~ConnectorPluginModelTests|FullyQualifiedName~SelectorExecutionEngineTests"
    }
}

Step "Optional PostgreSQL smoke" {
    if ($env:ConnectionStrings__Scout -and $env:ConnectionStrings__CustomerOps) {
        dotnet test .\tests\KynticAI.Scout.IntegrationTests\KynticAI.Scout.IntegrationTests.csproj --filter "FullyQualifiedName~BackendOnlyModeIntegrationTests"
    } else {
        Write-Host "Skipped: PostgreSQL connection strings are not set."
    }
}

Step "Optional backup restore dry run" {
    if ($env:PGHOST -and $env:PGUSER -and $env:SCOUT_DB) {
        pg_dump --schema-only --dbname=$env:SCOUT_DB | pg_restore --list | Out-Null
    } else {
        Write-Host "Skipped: PGHOST, PGUSER, and SCOUT_DB are not set."
    }
}

Step "Support bundle command safety" {
    $supportCommands = rg -n "support bundle|support-bundle|Generate.*SupportBundle" src docs scripts
    if ($supportCommands) {
        rg -n "rawSourceRecordsIncluded.*false|excludes raw|redact" docs src | Out-Null
    } else {
        Write-Host "Skipped: no public support bundle command exists."
    }
}

Step "Public forbidden-code scan" {
    $forbidden = rg -n "using KynticAIScout\.Enterprise|namespace KynticAIScout\.Enterprise|Scout\.Cloud\.Api|StripeSecret|OAuthRefreshToken|BEGIN PRIVATE KEY|service_account|Fortress|pgvector|Rust engine|vector DB|private LLM" src apps packages docs docs-site/src deploy tools
    if ($forbidden) { $forbidden; Fail "public forbidden-code scan found private implementation or secret markers." }
}

Write-Host "Pilot readiness checks completed."
