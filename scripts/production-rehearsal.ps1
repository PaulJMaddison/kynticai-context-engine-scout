[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:5198",
    [switch]$RunDocker,
    [switch]$RunMigrations,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message =="
}

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-OptIn([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    return $value -eq "1" -or $value -ieq "true"
}

function Require-OptIn([string]$Name, [string]$Purpose) {
    if (-not (Test-OptIn $Name)) {
        throw "$Purpose is opt-in. Set $Name=1 to run this proof path."
    }
}

function Resolve-Dotnet {
    $localDotnet = Join-Path (Get-Location) ".dotnet\dotnet.exe"
    if (Test-Path $localDotnet) {
        return $localDotnet
    }

    $globalDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($globalDotnet) {
        return $globalDotnet.Source
    }

    throw ".NET SDK is not available. Install the SDK from global.json, then rerun this rehearsal."
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

function Assert-Setting([string]$Name, [string]$Expected, [string]$Actual) {
    if ($Actual -ne $Expected) {
        throw "$Name must be '$Expected' for a production-style pilot rehearsal. Current value: '$Actual'."
    }
}

function Assert-NotPlaceholder([string]$Name, [string]$Value, [int]$MinLength) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -lt $MinLength -or $Value -match "development-only|change|replace|password|secret") {
        throw "$Name must be supplied from a secret store and be at least $MinLength characters. Current value is missing, short, or placeholder-like."
    }
}

Write-Step "Production-style configuration checks"
$environment = if ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT } else { "Production" }
$platformMode = if ($env:Platform__Mode) { $env:Platform__Mode } else { "SelfHosted" }
$databaseProvider = if ($env:Database__Provider) { $env:Database__Provider } else { "Postgres" }
$seedDemo = if ($env:Bootstrap__SeedDemoData) { $env:Bootstrap__SeedDemoData } else { "false" }
$demoFallback = if ($env:VITE_DEMO_FALLBACK) { $env:VITE_DEMO_FALLBACK } else { "false" }
$dataProtectionRequired = if ($env:DataProtection__RequirePersistentKeys) { $env:DataProtection__RequirePersistentKeys } else { "true" }

Assert-Setting "ASPNETCORE_ENVIRONMENT" "Production" $environment
if ($platformMode -notin @("SelfHosted", "ManagedDataPlane", "SaaS", "BackendOnly")) {
    throw "Platform__Mode must be SelfHosted or ManagedDataPlane for a production-style pilot rehearsal (BackendOnly/SaaS remain compatibility aliases). Current value: '$platformMode'."
}
Assert-Setting "Database__Provider" "Postgres" $databaseProvider
Assert-Setting "Bootstrap__SeedDemoData" "false" $seedDemo
Assert-Setting "VITE_DEMO_FALLBACK" "false" $demoFallback
Assert-Setting "DataProtection__RequirePersistentKeys" "true" $dataProtectionRequired
if ([string]::IsNullOrWhiteSpace($env:DataProtection__KeyRingPath)) {
    Write-Warning "DataProtection__KeyRingPath is not set in this shell. Use /var/lib/scout/data-protection-keys or another mounted, backed-up path."
}
Assert-NotPlaceholder "Auth__SigningKey" $env:Auth__SigningKey 48
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Scout)) {
    throw "ConnectionStrings__Scout must be set to the single Scout PostgreSQL connection string."
}

Write-Host "Configuration checks passed."

Write-Step ".NET build check"
$dotnet = Resolve-Dotnet
Invoke-Native $dotnet --info | Select-Object -First 20
if (-not $SkipBuild) {
    Invoke-Native $dotnet build .\KynticAI.Scout.slnx --disable-build-servers
}

Write-Step "Migration path"
Write-Host "Migration command:"
Write-Host "dotnet run --project .\src\KynticAI.Scout.Api\KynticAI.Scout.Api.csproj -- migrate"
if ($RunMigrations) {
    Invoke-Native $dotnet run --project .\src\KynticAI.Scout.Api\KynticAI.Scout.Api.csproj -- migrate
} else {
    Write-Host "Not running migrations because -RunMigrations was not supplied."
}

Write-Step "Backup and restore commands"
Write-Host "Backup (single Scout store):"
Write-Host "pg_dump --format=custom --file .\backup\scout_context_db.dump scout_context_db"
Write-Host "Restore into a disposable database:"
Write-Host "createdb scout_context_restore_check"
Write-Host "pg_restore --clean --if-exists --dbname scout_context_restore_check .\backup\scout_context_db.dump"

Write-Step "Docker/PostgreSQL rehearsal"
if ($RunDocker) {
    Require-OptIn "KYNTIC_RUN_EXTERNAL_DOTNET_TESTS" "Docker/PostgreSQL rehearsal"
    if (-not (Test-Command "docker")) {
        throw "Docker is not available. Install Docker or run the database checks in an environment with PostgreSQL."
    }
    docker compose up -d postgres
    docker compose ps postgres
} else {
    Write-Host "Not starting Docker because -RunDocker was not supplied."
    Write-Host "To run locally: docker compose up -d postgres"
}

Write-Step "Endpoint smoke checks"
foreach ($path in @("/health/live", "/health/ready", "/health", "/api/v1/health")) {
    try {
        $response = Invoke-RestMethod "$BaseUrl$path" -TimeoutSec 5
        Write-Host "$path responded: $($response | ConvertTo-Json -Compress)"
    } catch {
        Write-Warning "$path could not be reached at $BaseUrl. Start the API and rerun smoke checks. Error: $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "GraphQL endpoint: $BaseUrl/graphql"
Write-Host "REST endpoints: $BaseUrl/api/rest and $BaseUrl/api/v1"
Write-Host "Machine auth endpoint: $BaseUrl/api/auth/token"
$telemetryEndpoint = $env:Telemetry__OtlpEndpoint
if ([string]::IsNullOrWhiteSpace($telemetryEndpoint)) {
    $telemetryEndpoint = $env:OTEL_EXPORTER_OTLP_ENDPOINT
}
Write-Host "Logs: inspect host/container stdout and the configured OpenTelemetry collector. Telemetry endpoint: $telemetryEndpoint"
Write-Host "Production rehearsal completed. Review warnings before using this environment for a paid pilot."
