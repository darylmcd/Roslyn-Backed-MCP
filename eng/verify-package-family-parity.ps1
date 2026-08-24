[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$PackagesPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PackagesPath)) {
    $PackagesPath = Join-Path $RepoRoot 'Directory.Packages.props'
}
elseif (-not [System.IO.Path]::IsPathFullyQualified($PackagesPath)) {
    $PackagesPath = Join-Path $RepoRoot $PackagesPath
}

$PackagesPath = [System.IO.Path]::GetFullPath($PackagesPath)
if (-not [System.IO.File]::Exists($PackagesPath)) {
    throw "Central package file does not exist: '$PackagesPath'."
}

[xml]$document = [System.IO.File]::ReadAllText($PackagesPath)
$versions = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($node in @($document.Project.ItemGroup.PackageVersion)) {
    $packageId = [string]$node.Include
    $version = [string]$node.Version
    if ([string]::IsNullOrWhiteSpace($packageId) -or [string]::IsNullOrWhiteSpace($version)) {
        throw 'Directory.Packages.props contains a PackageVersion without a non-empty Include and Version.'
    }
    if (-not $versions.TryAdd($packageId, $version)) {
        throw "Directory.Packages.props contains duplicate PackageVersion rows for '$packageId'."
    }
}

$families = [ordered]@{
    'Microsoft.Build compile family' = @(
        'Microsoft.Build',
        'Microsoft.Build.Framework',
        'Microsoft.Build.Tasks.Core',
        'Microsoft.Build.Utilities.Core'
    )
    'Roslyn API family' = @(
        'Microsoft.CodeAnalysis.CSharp',
        'Microsoft.CodeAnalysis.Analyzers',
        'Microsoft.CodeAnalysis.CSharp.Workspaces',
        'Microsoft.CodeAnalysis.CSharp.Features',
        'Microsoft.CodeAnalysis.Features',
        'Microsoft.CodeAnalysis.Workspaces.MSBuild',
        'Microsoft.CodeAnalysis.CSharp.Scripting'
    )
    'Microsoft.Extensions runtime family' = @(
        'Microsoft.Extensions.Hosting',
        'Microsoft.Extensions.Http',
        'Microsoft.Extensions.Logging',
        'Microsoft.Extensions.Logging.Console'
    )
    'MSTest family' = @(
        'MSTest.TestAdapter',
        'MSTest.TestFramework'
    )
}

$issues = [System.Collections.Generic.List[string]]::new()
foreach ($family in $families.GetEnumerator()) {
    $familyVersions = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($packageId in $family.Value) {
        if (-not $versions.ContainsKey($packageId)) {
            $issues.Add("$($family.Key) is missing central pin '$packageId'.")
            continue
        }
        $familyVersions[$packageId] = $versions[$packageId]
    }

    if (($familyVersions.Values | Sort-Object -Unique).Count -gt 1) {
        $rendered = $familyVersions.GetEnumerator() |
            Sort-Object Key |
            ForEach-Object { "$($_.Key)=$($_.Value)" }
        $issues.Add("$($family.Key) pins must match: $($rendered -join ', ').")
    }
}

if ($issues.Count -gt 0) {
    throw (($issues | Sort-Object -Unique) -join [Environment]::NewLine)
}

Write-Host "Package family parity passed for $($families.Count) coordinated families."
