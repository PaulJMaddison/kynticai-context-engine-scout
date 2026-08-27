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

Step "GitHub Actions workflow safety" {
    $workflows = @(Get-ChildItem ".github/workflows\*" -File -Force -Include "*.yml","*.yaml")
    if (-not $workflows) {
        Write-Host "No active GitHub Actions workflow files found; CI/CD is currently disabled (workflows are renamed to .disabled)."
        return
    }
    $forbiddenPatterns = @(
        '\$\{\{\s*secrets\.',
        '-----BEGIN',
        'AKIA[0-9A-Z]{16}',
        'ghp_[A-Za-z0-9]{20,}',
        'github_pat_[A-Za-z0-9_]{20,}',
        'xox[baprs]-',
        'git@github\.com',
        'repository_dispatch'
    )
    foreach ($workflow in $workflows) {
        $content = Get-Content $workflow.FullName -Raw
        foreach ($pattern in $forbiddenPatterns) {
            if ($content -match $pattern) {
                Fail "workflow $($workflow.Name) contains forbidden content matching: $pattern"
            }
        }
        if ($content -match '\b(playwright|test:e2e)\b' -and $content -notmatch 'KYNTIC_RUN_BROWSER_TESTS') {
            Fail "workflow $($workflow.Name) has browser steps without the KYNTIC_RUN_BROWSER_TESTS opt-in."
        }
        if ($content -match '\bdocker\b' -and $content -notmatch 'KYNTIC_RUN_EXTERNAL_DOTNET_TESTS') {
            Fail "workflow $($workflow.Name) has container steps without the KYNTIC_RUN_EXTERNAL_DOTNET_TESTS opt-in."
        }
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
        if (-not $env:ConnectionStrings__Scout) {
            Fail "Production mode requires ConnectionStrings__Scout for the single Scout PostgreSQL store."
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
    if ($env:ConnectionStrings__Scout) {
        dotnet test .\tests\KynticAI.Scout.IntegrationTests\KynticAI.Scout.IntegrationTests.csproj --filter "FullyQualifiedName~BackendOnlyModeIntegrationTests"
    } else {
        Write-Host "Skipped: the Scout PostgreSQL connection string is not set."
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
    & .\scripts\public-safety-scan.ps1
    if ($LASTEXITCODE -ne 0) {
        Fail "public forbidden-code scan found private implementation or secret markers."
    }
}

Write-Host "Pilot readiness checks completed."
