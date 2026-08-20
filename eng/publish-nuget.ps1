param(
    [string]$Version,
    [string]$Source = 'https://api.nuget.org/v3/index.json',
    [string]$PackageId = 'Darylmcd.RoslynMcp',
    [switch]$NoPush
)

# Publishes the Darylmcd.RoslynMcp global tool to nuget.org with the same
# provenance guarantees as the publish-nuget workflow
# (.github/workflows/publish-nuget.yml):
#   1. The full publish-mode release gate runs first (verify-release.ps1:
#      version drift across the six canonical version files, build, tests,
#      publish smoke, consumed changelog fragments, breaking-version bump).
#      Expect ~10-15 minutes locally — the same cost the CI path pays.
#   2. The version is derived from the canonical Directory.Build.props.
#      A supplied -Version is an ASSERTION that must match; it never overrides.
#   3. The package is packed fresh into an owned staging directory
#      (artifacts/publish-nuget/<version>/, recreated per run) — a stale
#      package left in nupkg/ can never be selected.
#   4. -NoPush validates and packs only (dry run), then exits 0.
#   5. A nonzero push exit fails the script; `Done.` prints
#      only on a verified-successful push.
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

# Fail fast on a missing key before spending ~10-15 minutes in the release gate.
$apiKey = $env:NUGET_API_KEY
if (-not $NoPush -and [string]::IsNullOrWhiteSpace($apiKey)) {
    throw @"
NUGET_API_KEY is not set. Create a key at https://www.nuget.org/account/apikeys then:
  `$env:NUGET_API_KEY = '<your-key>'
  ./eng/publish-nuget.ps1
Or run a validate-and-pack-only dry run: ./eng/publish-nuget.ps1 -NoPush
"@
}

# Manual publishing has the same release contract as the tag/release workflow:
# run the full publish-mode release gate before anything is packed or pushed.
# PowerShell does not honor $ErrorActionPreference = 'Stop' for a child
# script's nonzero `exit`, so reset the global native-exit state and capture it
# immediately after the child returns.
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot 'verify-release.ps1') `
    -Configuration Release `
    -NoCoverage `
    -RequireConsumedFragments
$releaseGateExitCode = $global:LASTEXITCODE
if ($releaseGateExitCode -ne 0) {
    throw "Release validation failed with exit code $releaseGateExitCode."
}

# The publishable version is ALWAYS the canonical Directory.Build.props value
# (verify-release.ps1 has already proven the other five version files agree).
# A caller-supplied -Version is a cross-check, never an override.
[xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
$canonicalVersion = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $canonicalVersion) {
    throw 'Could not read Version from Directory.Build.props'
}
if ($Version -and $Version -ne $canonicalVersion) {
    throw "-Version $Version disagrees with the canonical Directory.Build.props version $canonicalVersion. The parameter is an assertion, not an override; fix the checkout (or drop the parameter) instead of publishing an off-checkout version."
}
$Version = $canonicalVersion

# Pack fresh into an owned staging root so a stale artifact from an earlier
# release can never be selected. Never reuse the shared nupkg/ directory.
$stagingRoot = Join-Path $repoRoot 'artifacts' 'publish-nuget' $Version
if (Test-Path $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

Write-Host "Packing $PackageId $Version into $stagingRoot ..."
$global:LASTEXITCODE = 0
dotnet pack (Join-Path $repoRoot 'src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj') `
    -c Release `
    -o $stagingRoot
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

$packagePath = Join-Path $stagingRoot "$PackageId.$Version.nupkg"
if (-not (Test-Path $packagePath)) {
    $produced = @(Get-ChildItem -Path $stagingRoot -Filter '*.nupkg' | ForEach-Object { $_.Name }) -join ', '
    throw "Fresh pack did not produce the expected $PackageId.$Version.nupkg in $stagingRoot (found: $produced)."
}

if ($NoPush) {
    Write-Host "Validate-only run complete (-NoPush): packed $packagePath; nothing was pushed."
    exit 0
}

Write-Host "Pushing $packagePath to nuget.org ..."
$global:LASTEXITCODE = 0
dotnet nuget push $packagePath --source $Source --api-key $apiKey --skip-duplicate
if ($LASTEXITCODE -ne 0) {
    throw "dotnet nuget push failed with exit code $LASTEXITCODE."
}
Write-Host 'Done.'
