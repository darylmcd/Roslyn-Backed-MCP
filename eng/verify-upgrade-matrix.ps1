[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$PackagesPath,
    [string]$MatrixPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PackagesPath)) {
    $PackagesPath = Join-Path $RepoRoot 'Directory.Packages.props'
}
elseif (-not [System.IO.Path]::IsPathFullyQualified($PackagesPath)) {
    $PackagesPath = Join-Path $RepoRoot $PackagesPath
}

if ([string]::IsNullOrWhiteSpace($MatrixPath)) {
    $MatrixPath = Join-Path $RepoRoot 'docs/upgrade-matrix.md'
}
elseif (-not [System.IO.Path]::IsPathFullyQualified($MatrixPath)) {
    $MatrixPath = Join-Path $RepoRoot $MatrixPath
}

$PackagesPath = [System.IO.Path]::GetFullPath($PackagesPath)
$MatrixPath = [System.IO.Path]::GetFullPath($MatrixPath)
if (-not [System.IO.File]::Exists($PackagesPath)) {
    throw "Central package file does not exist: '$PackagesPath'."
}
if (-not [System.IO.File]::Exists($MatrixPath)) {
    throw "Upgrade matrix does not exist: '$MatrixPath'."
}

[xml]$centralDocument = [System.IO.File]::ReadAllText($PackagesPath)
$centralPackages = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
$duplicateCentralPackages = [System.Collections.Generic.List[string]]::new()
foreach ($node in @($centralDocument.Project.ItemGroup.PackageVersion)) {
    $packageId = [string]$node.Include
    $version = [string]$node.Version
    if ([string]::IsNullOrWhiteSpace($packageId) -or [string]::IsNullOrWhiteSpace($version)) {
        throw "Directory.Packages.props contains a PackageVersion without a non-empty Include and Version."
    }

    if (-not $centralPackages.TryAdd($packageId, $version)) {
        $duplicateCentralPackages.Add($packageId)
    }
}
if ($duplicateCentralPackages.Count -gt 0) {
    throw 'Directory.Packages.props contains duplicate PackageVersion rows: ' +
        (($duplicateCentralPackages | Sort-Object -Unique) -join ', ')
}

$documentedPackages = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
$issues = [System.Collections.Generic.List[string]]::new()
$rowPattern = '^\|\s*`(?<package>[^`]+)`\s*\|\s*`(?<version>[^`]+)`\s*\|\s*`Directory\.Packages\.props`\s*\|.*\|\s*$'
$matrixLines = [System.IO.File]::ReadAllLines($MatrixPath)
for ($index = 0; $index -lt $matrixLines.Length; $index++) {
    $line = $matrixLines[$index]
    if (-not $line.TrimStart().StartsWith('|', [System.StringComparison]::Ordinal) -or
        -not $line.Contains('`Directory.Packages.props`', [System.StringComparison]::Ordinal)) {
        continue
    }

    $match = [regex]::Match($line, $rowPattern)
    if (-not $match.Success) {
        $issues.Add("Upgrade matrix line $($index + 1) is a malformed central-package row: $line")
        continue
    }

    $packageId = $match.Groups['package'].Value
    $version = $match.Groups['version'].Value
    if (-not $documentedPackages.TryAdd($packageId, $version)) {
        $issues.Add("Upgrade matrix contains duplicate rows for '$packageId'.")
    }
}

foreach ($entry in $documentedPackages.GetEnumerator()) {
    if (-not $centralPackages.ContainsKey($entry.Key)) {
        $issues.Add("Upgrade matrix documents '$($entry.Key)' as centrally pinned, but Directory.Packages.props has no matching package.")
        continue
    }

    $centralVersion = $centralPackages[$entry.Key]
    if ($entry.Value -cne $centralVersion) {
        $issues.Add("Upgrade matrix documents '$($entry.Key)' at '$($entry.Value)' but Directory.Packages.props pins '$centralVersion'.")
    }
}

foreach ($entry in $centralPackages.GetEnumerator()) {
    if (-not $documentedPackages.ContainsKey($entry.Key)) {
        $issues.Add("Upgrade matrix has no central-package row for '$($entry.Key)' pinned at '$($entry.Value)'.")
    }
}

if ($issues.Count -gt 0) {
    throw ($issues | Sort-Object -Unique) -join [Environment]::NewLine
}

Write-Host "Upgrade matrix parity passed for $($centralPackages.Count) central package pins."
