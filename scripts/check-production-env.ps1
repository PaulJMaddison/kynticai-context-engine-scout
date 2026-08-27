[CmdletBinding()]
param(
    [string]$EnvFile = ".env",
    [switch]$AllowDemoData
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$settings = @{}

function Import-EnvFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return
    }

    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#") -or $trimmed -notmatch "=") {
            continue
        }

        $name, $value = $trimmed.Split("=", 2)
        $settings[$name.Trim()] = $value.Trim().Trim('"').Trim("'")
    }
}

function Get-Setting {
    param([string]$Name)
    $envValue = [Environment]::GetEnvironmentVariable($Name)
    if (-not [string]::IsNullOrWhiteSpace($envValue)) {
        return $envValue
    }

    if ($settings.ContainsKey($Name)) {
        return [string]$settings[$Name]
    }

    return ""
}

function Is-True {
    param([string]$Value)
    return $Value -match "^(1|true|yes|on)$"
}

function Is-Placeholder {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    return $Value -match "(replace|change|placeholder|example|demo|development-only|password|secret|localhost;|Data Source=\.demo-data)"
}

$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

Import-EnvFile $EnvFile

$platformMode = Get-Setting "Platform__Mode"
$databaseProvider = Get-Setting "Database__Provider"
$contextConnection = Get-Setting "ConnectionStrings__Scout"
$signingKey = Get-Setting "Auth__SigningKey"
$demoFallback = Get-Setting "VITE_DEMO_FALLBACK"
$seedDemoData = Get-Setting "Bootstrap__SeedDemoData"
$demoExperience = Get-Setting "FeatureFlags__DemoExperience"
$keyRingPath = Get-Setting "DataProtection__KeyRingPath"
$persistentKeys = Get-Setting "DataProtection__RequirePersistentKeys"

if ($demoFallback -ne "false") {
    $failures.Add("VITE_DEMO_FALLBACK must be false for production-style builds.")
}

if (Is-Placeholder $signingKey -or $signingKey.Length -lt 48) {
    $failures.Add("Auth__SigningKey must be supplied from a secret store and be at least 48 characters; placeholder values are refused.")
}

if ($platformMode -eq "LocalDemo" -or [string]::IsNullOrWhiteSpace($platformMode)) {
    $failures.Add("Platform__Mode must be SelfHosted or ManagedDataPlane for new production deployments (BackendOnly/SaaS are legacy compatibility values).")
}

if ($databaseProvider -ne "Postgres") {
    $failures.Add("Database__Provider must be Postgres for production-style deployment.")
}

if ($contextConnection -match "Data Source=|Sqlite|\.db|\.sqlite") {
    $failures.Add("SQLite/local database connection strings are not acceptable for production-style deployment.")
}

if ([string]::IsNullOrWhiteSpace($contextConnection)) {
    $failures.Add("ConnectionStrings__Scout must be configured.")
}

if ($contextConnection -notmatch "Host=|Server=|Database=") {
    $failures.Add("ConnectionStrings__Scout must look like a PostgreSQL connection string.")
}

if (-not $AllowDemoData) {
    if (Is-True $seedDemoData) {
        $failures.Add("Bootstrap__SeedDemoData must be false unless -AllowDemoData is explicitly supplied for a rehearsal.")
    }

    if (Is-True $demoExperience) {
        $failures.Add("FeatureFlags__DemoExperience must be false for customer/prod-style deployments.")
    }
}

if ([string]::IsNullOrWhiteSpace($keyRingPath) -or $keyRingPath -match "\.demo-data|temp|tmp") {
    $failures.Add("DataProtection__KeyRingPath must be a persistent mounted path, not a demo or temporary path.")
}

if ($persistentKeys -ne "true") {
    $failures.Add("DataProtection__RequirePersistentKeys must be true for production-style deployment.")
}

Write-Host "Public data-plane production environment preflight"
Write-Host "Environment file: $EnvFile"
Write-Host "Platform__Mode: $platformMode"
Write-Host "Database__Provider: $databaseProvider"
Write-Host "VITE_DEMO_FALLBACK: $demoFallback"
Write-Host "Bootstrap__SeedDemoData: $seedDemoData"
Write-Host "FeatureFlags__DemoExperience: $demoExperience"
Write-Host "DataProtection__KeyRingPath configured: $(-not [string]::IsNullOrWhiteSpace($keyRingPath))"
Write-Host "Scout connection string configured: $(-not [string]::IsNullOrWhiteSpace($contextConnection))"

foreach ($warning in $warnings) {
    Write-Warning $warning
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Preflight failed:"
    foreach ($failure in $failures) {
        Write-Host "- $failure"
    }
    exit 1
}

Write-Host "Preflight passed. No secrets were printed."
