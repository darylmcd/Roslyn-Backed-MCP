param(
    [string]$Version,
    [string]$Source = 'https://api.nuget.org/v3/index.json',
    [string]$PackageId = 'Darylmcd.RoslynMcp'
)

# Publishes the packed Darylmcd.RoslynMcp global tool to nuget.org.
# Build the package first (creates nupkg\Darylmcd.RoslynMcp.<Version>.nupkg), e.g.:
#   dotnet publish src/RoslynMcp.Host.Stdio -c Release
#
# Authentication: API key from https://www.nuget.org/account/apikeys (Push scope).
#   $env:NUGET_API_KEY = '<secret>'
#   ./eng/publish-nuget.ps1
#
# Note: the unprefixed `RoslynMcp` package id was taken on nuget.org by another
# publisher (chrismo80, v1.1.1) on 2026-04-08, hence the `Darylmcd.` prefix.
#
# Do not commit API keys.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
    if (-not $Version) {
        throw 'Could not read Version from Directory.Build.props'
    }
}

$packagePath = Join-Path $repoRoot "nupkg\$PackageId.$Version.nupkg"
if (-not (Test-Path $packagePath)) {
    throw "Package not found: $packagePath`nBuild it with: dotnet publish src/RoslynMcp.Host.Stdio -c Release"
}

$apiKey = $env:NUGET_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw @"
NUGET_API_KEY is not set. Create a key at https://www.nuget.org/account/apikeys then:
  `$env:NUGET_API_KEY = '<your-key>'
  ./eng/publish-nuget.ps1
"@
}

Write-Host "Pushing $packagePath to nuget.org ..."
dotnet nuget push $packagePath --source $Source --api-key $apiKey --skip-duplicate
Write-Host 'Done.'
