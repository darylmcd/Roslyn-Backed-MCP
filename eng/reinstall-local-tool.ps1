[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageSource,

    [string]$Version = '',

    [string]$ProjectPath = '',

    [ValidateRange(0, [int]::MaxValue)]
    [int]$OwnedProcessId = 0,

    [string]$OwnedProcessStartedAtUtc = '',

    [string]$ToolStoreRoot = (Join-Path $env:USERPROFILE '.dotnet' 'tools')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'stop-owned-tool-store-process.ps1')

$currentPackageId = 'Darylmcd.RoslynMcp'
$legacyPackageId = 'RoslynMcp'

if (-not $PSBoundParameters.ContainsKey('OwnedProcessId') -and
    -not [string]::IsNullOrWhiteSpace($env:ROSLYNMCP_REINSTALL_PROCESS_ID)) {
    $OwnedProcessId = [int]$env:ROSLYNMCP_REINSTALL_PROCESS_ID
}
if (-not $PSBoundParameters.ContainsKey('OwnedProcessStartedAtUtc') -and
    -not [string]::IsNullOrWhiteSpace($env:ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC)) {
    $OwnedProcessStartedAtUtc = $env:ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC
}

function Invoke-DotnetStep {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [switch]$CaptureOutput
    )

    $lines = [Collections.Generic.List[string]]::new()
    $global:LASTEXITCODE = 0
    & dotnet @Arguments 2>&1 | ForEach-Object {
        $line = $_.ToString()
        [void]$lines.Add($line)
        if (-not $CaptureOutput) {
            Write-Host $line
        }
    }
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode. If the global tool is in use, supply the PID and round-trip UTC start time of the roslynmcp process owned by this reinstall operation."
    }

    if ($CaptureOutput) {
        return $lines.ToArray()
    }
}

$resolvedPackageSource = (Resolve-Path -LiteralPath $PackageSource -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedPackageSource -PathType Container)) {
    throw "Package source is not a directory: $resolvedPackageSource"
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        throw 'Supply Version or ProjectPath so the reinstall version can be resolved.'
    }

    $resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop).Path
    $versionOutput = Invoke-DotnetStep `
        -Description 'Local tool version discovery' `
        -Arguments @('msbuild', '-nologo', $resolvedProjectPath, '-getProperty:Version') `
        -CaptureOutput
    $Version = ($versionOutput -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw 'Local tool version discovery returned an empty value.'
    }
}

Stop-OwnedToolStoreProcess `
    -OwnedProcessId $OwnedProcessId `
    -OwnedProcessStartedAtUtc $OwnedProcessStartedAtUtc `
    -ToolStoreRoot $ToolStoreRoot

$inventoryJson = Invoke-DotnetStep `
    -Description 'Global tool inventory' `
    -Arguments @('tool', 'list', '-g', '--format', 'json') `
    -CaptureOutput
try {
    $inventory = ($inventoryJson -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $inventory.data) {
        throw 'Missing data array.'
    }
}
catch {
    throw 'Global tool inventory did not return the expected JSON contract.'
}

$installedPackageIds = @($inventory.data | ForEach-Object { [string]$_.packageId })
foreach ($packageId in @($currentPackageId, $legacyPackageId)) {
    if ($installedPackageIds -contains $packageId) {
        Invoke-DotnetStep `
            -Description "Global tool uninstall ($packageId)" `
            -Arguments @('tool', 'uninstall', '-g', $packageId)
    }
}

Invoke-DotnetStep `
    -Description "Global tool install ($currentPackageId $Version)" `
    -Arguments @(
        'tool', 'install', '-g', $currentPackageId,
        '--add-source', $resolvedPackageSource,
        '--version', $Version)

Write-Host "Installed $currentPackageId $Version from $resolvedPackageSource."
