<#
.SYNOPSIS
Generate the deterministic repository formatter baseline inventory.

.DESCRIPTION
Runs `dotnet format RoslynMcp.slnx --verify-no-changes --no-restore`, parses every
reported diagnostic, and emits a stable JSON inventory of the repository's current
formatter debt. The inventory records what is broken; it never repairs, suppresses,
or relabels anything.

Determinism contract:
  * No timestamp is emitted. `generatedAt` is deliberately absent so that two runs
    against an unchanged tree produce byte-identical output and a clean `git diff`
    against the tracked artifact.
  * Every collection is sorted with ordinal (not culture) comparison.
  * Findings are de-duplicated on path+id+line+column, because a file owned by more
    than one project is reported once per owning project.
  * Paths are normalized to repo-relative forward-slash form.
  * Both `error` and `warning` severities are captured. Severity is a per-project
    MSBuild concern (`TreatWarningsAsErrors`), not a property of the formatter
    finding; filtering on it would silently hide real debt.

Fail-closed contract:
  `--verify-no-changes` alone is NOT deterministic. On a tree whose packages are not
  restored, `dotnet format` skips the affected projects and prints
  "Required references did not load", yielding a silently truncated inventory
  (observed: 285 findings unrestored vs 382 restored on the same tree). This script
  therefore restores first (suppress with -NoRestore) and throws if the truncation
  marker appears in the formatter output.

Exit codes of `dotnet format --verify-no-changes`: 0 = clean, 2 = findings reported.
Both are success here; any other exit code is fatal.

.PARAMETER Check
Regenerate the inventory in memory and compare it against the tracked artifact
without writing. Exits 1 when they differ. CI and the contract test share this path.

.PARAMETER NoRestore
Skip the `dotnet restore` precondition. Only safe when the caller has already
restored the solution in this working tree.
#>
param(
    [switch]$Check,

    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath 'format-diagnostic-contract.ps1')

$schemaVersion = 1
$solutionFileName = 'RoslynMcp.slnx'
$formatArguments = @('format', $solutionFileName, '--verify-no-changes', '--no-restore')

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$solutionPath = Join-Path -Path $repositoryRoot -ChildPath $solutionFileName
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Solution not found: $solutionPath"
}

# Fixed artifact location, resolved from the script's own directory so the script
# behaves identically no matter which directory it is invoked from.
$OutputPath = Join-Path -Path $PSScriptRoot -ChildPath 'format-baseline.json'

function ConvertTo-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Formatter reported a path outside the repository: $full"
    }

    return $full.Substring($prefix.Length).Replace('\', '/')
}

Push-Location -LiteralPath $repositoryRoot
try {
    if (-not $NoRestore) {
        $global:LASTEXITCODE = 0
        $restoreOutput = @(& dotnet restore $solutionFileName)
        if ($global:LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $($global:LASTEXITCODE):`n$($restoreOutput -join [System.Environment]::NewLine)"
        }
    }

    $global:LASTEXITCODE = 0
    $formatOutput = @(& dotnet @formatArguments 2>&1 | ForEach-Object { $_.ToString() })
    $formatExitCode = $global:LASTEXITCODE
}
finally {
    Pop-Location
}

if ($formatExitCode -ne 0 -and $formatExitCode -ne 2) {
    throw "dotnet format exited with unexpected code $formatExitCode.`n$($formatOutput -join [System.Environment]::NewLine)"
}

$truncated = @($formatOutput | Where-Object { $_ -like "*$formatTruncationMarker*" })
if ($truncated.Count -gt 0) {
    throw "dotnet format produced a truncated inventory (unrestored projects were skipped): $($truncated[0])"
}

$findingKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$findingsByFile = @{}

foreach ($line in $formatOutput) {
    $match = [regex]::Match($line, $formatDiagnosticPattern)
    if (-not $match.Success) {
        continue
    }

    $relativePath = ConvertTo-RepositoryRelativePath -Path $match.Groups['path'].Value
    $diagnosticId = $match.Groups['id'].Value
    $key = "$relativePath|$diagnosticId|$($match.Groups['line'].Value)|$($match.Groups['column'].Value)"
    if (-not $findingKeys.Add($key)) {
        continue
    }

    if (-not $findingsByFile.ContainsKey($relativePath)) {
        $findingsByFile[$relativePath] = @{}
    }

    $perFile = $findingsByFile[$relativePath]
    if (-not $perFile.ContainsKey($diagnosticId)) {
        $perFile[$diagnosticId] = 0
    }

    $perFile[$diagnosticId] = $perFile[$diagnosticId] + 1
}

$ordinal = [System.StringComparer]::Ordinal
$sortedPaths = [string[]]@($findingsByFile.Keys)
[System.Array]::Sort($sortedPaths, $ordinal)

$files = [System.Collections.Generic.List[object]]::new()
$totalsByDiagnosticId = @{}
$findingCount = 0

foreach ($path in $sortedPaths) {
    $perFile = $findingsByFile[$path]
    $sortedIds = [string[]]@($perFile.Keys)
    [System.Array]::Sort($sortedIds, $ordinal)

    $countsByDiagnosticId = [ordered]@{}
    $fileFindingCount = 0
    foreach ($id in $sortedIds) {
        $count = $perFile[$id]
        $countsByDiagnosticId[$id] = $count
        $fileFindingCount += $count
        if (-not $totalsByDiagnosticId.ContainsKey($id)) {
            $totalsByDiagnosticId[$id] = 0
        }

        $totalsByDiagnosticId[$id] = $totalsByDiagnosticId[$id] + $count
    }

    $findingCount += $fileFindingCount
    $files.Add([ordered]@{
            path                 = $path
            findingCount         = $fileFindingCount
            diagnosticIds        = $sortedIds
            countsByDiagnosticId = $countsByDiagnosticId
        })
}

$sortedDiagnosticIds = [string[]]@($totalsByDiagnosticId.Keys)
[System.Array]::Sort($sortedDiagnosticIds, $ordinal)
$orderedTotalsByDiagnosticId = [ordered]@{}
foreach ($id in $sortedDiagnosticIds) {
    $orderedTotalsByDiagnosticId[$id] = $totalsByDiagnosticId[$id]
}

$inventory = [ordered]@{
    schemaVersion = $schemaVersion
    command       = "dotnet $($formatArguments -join ' ')"
    diagnosticIds = $sortedDiagnosticIds
    totals        = [ordered]@{
        findingCount         = $findingCount
        fileCount            = $files.Count
        countsByDiagnosticId = $orderedTotalsByDiagnosticId
    }
    files         = $files.ToArray()
}

$json = ($inventory | ConvertTo-Json -Depth 8).Replace("`r`n", "`n").TrimEnd("`n") + "`n"

if ($Check) {
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Formatter baseline artifact not found: $OutputPath. Run eng/generate-format-baseline.ps1 to create it."
    }

    $tracked = [System.IO.File]::ReadAllText($OutputPath).Replace("`r`n", "`n")
    Write-Output $json
    if ($tracked -ne $json) {
        Write-Error "Formatter baseline artifact is stale. Regenerate it with eng/generate-format-baseline.ps1."
        exit 1
    }

    exit 0
}

[System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output $json
exit 0
