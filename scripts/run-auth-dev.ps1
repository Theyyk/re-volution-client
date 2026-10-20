$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'auth\REvolutionClient.Auth.csproj'

if ([string]::IsNullOrWhiteSpace($env:REVOLUTION_AUTH_DB)) {
    $env:REVOLUTION_AUTH_DB = Join-Path $env:LOCALAPPDATA 'REvolutionClient\auth\auth.db'
}

if ([string]::IsNullOrWhiteSpace($env:REVOLUTION_AUTH_URL)) {
    $env:REVOLUTION_AUTH_URL = 'http://127.0.0.1:25567'
}

Write-Host "RE:volution Auth URL: $env:REVOLUTION_AUTH_URL"
Write-Host "RE:volution Auth DB : $env:REVOLUTION_AUTH_DB"

dotnet run --project $project -c Release
