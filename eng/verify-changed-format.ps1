<#
.SYNOPSIS
Changed-file formatter gate: fail the build on formatter debt a pull request introduces.

.DESCRIPTION
The repository carries a large, explicitly tracked formatter baseline (see
`eng/format-baseline.json`, produced by `eng/generate-format-baseline.ps1`). A whole-repo
`dotnet format --verify-no-changes` gate cannot be enabled while that debt exists — it would
red every pull request. This script gates the only thing a pull request is responsible for:
the files it actually touches, minus the debt those files already carried.

Algorithm:
  1. Resolve the changed set with `git diff --name-only --diff-filter=ACMR <BaseRef>...HEAD`,
     filtered to `*.cs` files that still exist on disk. An empty set exits 0 immediately.
  2. Run `dotnet format <solution> --verify-no-changes --no-restore` scoped to that set and
     parse every reported diagnostic.
  3. Bucket the findings per (file, diagnostic id) and compare the observed count against the
     count the tracked baseline records for that same pair.
       * observed <= baseline  -> PRE-EXISTING. Reported as explicitly tracked debt, not a failure.
       * observed >  baseline  -> NEW. Fails the gate.
     Counting (rather than merely testing for presence) is what closes the concealment hole: a
     file that already carries one baseline `IDE1006` cannot smuggle in a second one.

Scoping contract:
  Findings are filtered to the changed set in PowerShell regardless of what `--include` does.
  `--include` is still passed as an optimization (verified empirically to scope both the report
  and the exit code), but correctness never depends on it. When the changed set is large enough
  that `--include` would risk the operating-system command-line limit, the argument is dropped
  and the solution-wide scan is narrowed by the same PowerShell filter.

Rename contract:
  `--diff-filter=ACMR` includes renames, and the baseline is keyed by path. A file moved to a new
  path therefore reports its inherited debt as NEW. That is deliberate: a rename is the natural
  moment to repair a file's formatter debt, and the alternative (following renames) would let debt
  travel silently forever. Repair the file, or regenerate the baseline in the same pull request.

Fail-closed contract:
  `dotnet format` silently skips projects whose references did not load, which would shrink the
  report to nothing and pass the gate. The truncation marker is treated as fatal, and the script
  restores first unless `-NoRestore` says the caller already did.

.PARAMETER BaseRef
Git ref the pull request is measured against. Defaults to `origin/main`.

.PARAMETER BaselinePath
Tracked formatter baseline inventory. Relative paths resolve against the repository root.

.PARAMETER SolutionPath
Solution or project `dotnet format` runs against. Relative paths resolve against the repository root.

.PARAMETER NoRestore
Skip the `dotnet restore` precondition. Only safe when the caller already restored this tree.

.PARAMETER Quiet
Suppress the per-file informational listing. Failures and the terminal summary are always printed.
#>
param(
    [string]$BaseRef = "origin/main",

    [string]$BaselinePath = "eng/format-baseline.json",

    [string]$SolutionPath = "RoslynMcp.slnx",

    [switch]$NoRestore,

    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Beyond this many characters of `--include` payload the argument is dropped and the
# PowerShell-side filter alone scopes the result. Keeps the gate correct on rename-heavy or
# very wide pull requests instead of failing on a command line the OS refuses to launch.
$includeArgumentBudget = 20000
$truncationMarker = "Required references did not load"
$gatedDiagnosticIds = @("FINALNEWLINE", "IDE1006", "IMPORTS", "WHITESPACE")

function Resolve-RepositoryRoot {
    $global:LASTEXITCODE = 0
    $root = (& git rev-parse --show-toplevel 2>&1 | Select-Object -First 1)
    if ($global:LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "verify-changed-format must run inside a git working tree; 'git rev-parse --show-toplevel' failed: $root"
    }

    return [System.IO.Path]::GetFullPath($root.Trim())
}

function Resolve-AgainstRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    if ([System.IO.Path]::IsPathFullyQualified($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path -Path $Root -ChildPath $Path))
}

function ConvertTo-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = $Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return $full.Substring($prefix.Length).Replace("\", "/")
}

$repositoryRoot = Resolve-RepositoryRoot
$resolvedSolutionPath = Resolve-AgainstRoot -Path $SolutionPath -Root $repositoryRoot
if (-not [System.IO.File]::Exists($resolvedSolutionPath)) {
    throw "Formatter gate solution does not exist: '$resolvedSolutionPath'."
}

$resolvedBaselinePath = Resolve-AgainstRoot -Path $BaselinePath -Root $repositoryRoot
if (-not [System.IO.File]::Exists($resolvedBaselinePath)) {
    throw "Formatter baseline inventory does not exist: '$resolvedBaselinePath'. Generate it with eng/generate-format-baseline.ps1."
}

# Baseline shape: { files: [ { path, countsByDiagnosticId: { <id>: <count> } } ] }.
# Flattened to "<path>|<id>" -> count so a lookup miss is unambiguously "no tracked debt".
$baselineDocument = Get-Content -LiteralPath $resolvedBaselinePath -Raw | ConvertFrom-Json
$baselineCounts = @{}
foreach ($entry in @($baselineDocument.files)) {
    $entryPath = [string]$entry.path
    foreach ($property in $entry.countsByDiagnosticId.PSObject.Properties) {
        $baselineCounts["$entryPath|$($property.Name)"] = [int]$property.Value
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    $global:LASTEXITCODE = 0
    $diffOutput = @(& git diff --name-only --diff-filter=ACMR "$BaseRef...HEAD" -- "*.cs" 2>&1 | ForEach-Object { $_.ToString() })
    if ($global:LASTEXITCODE -ne 0) {
        throw "Unable to diff against '$BaseRef'. Fetch the base ref (CI uses fetch-depth: 0) before running the formatter gate.`n$($diffOutput -join [System.Environment]::NewLine)"
    }

    $changedFiles = [string[]]@(
        $diffOutput |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim().Replace("\", "/") } |
            Where-Object { [System.IO.File]::Exists((Join-Path -Path $repositoryRoot -ChildPath $_)) } |
            Sort-Object -Unique
    )

    if ($changedFiles.Count -eq 0) {
        Write-Host "Changed-file formatter gate: no changed C# files against '$BaseRef'; nothing to verify."
        exit 0
    }

    if (-not $Quiet) {
        Write-Host "Changed-file formatter gate: verifying $($changedFiles.Count) changed C# file(s) against '$BaseRef'."
    }

    if (-not $NoRestore) {
        $global:LASTEXITCODE = 0
        $restoreOutput = @(& dotnet restore $resolvedSolutionPath 2>&1 | ForEach-Object { $_.ToString() })
        if ($global:LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $($global:LASTEXITCODE):`n$($restoreOutput -join [System.Environment]::NewLine)"
        }
    }

    $formatArguments = [System.Collections.Generic.List[string]]::new()
    $formatArguments.Add("format")
    $formatArguments.Add($resolvedSolutionPath)
    $formatArguments.Add("--verify-no-changes")
    $formatArguments.Add("--no-restore")

    $includePayloadLength = ($changedFiles | Measure-Object -Property Length -Sum).Sum + $changedFiles.Count
    if ($includePayloadLength -le $includeArgumentBudget) {
        $formatArguments.Add("--include")
        foreach ($changedFile in $changedFiles) {
            $formatArguments.Add($changedFile)
        }
    }
    elseif (-not $Quiet) {
        Write-Host "Changed-file formatter gate: changed set too wide for --include; scanning the solution and filtering to the changed set."
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

$truncated = @($formatOutput | Where-Object { $_ -like "*$truncationMarker*" })
if ($truncated.Count -gt 0) {
    throw "dotnet format produced a truncated report (unrestored projects were skipped), so the gate cannot prove the changed files are clean: $($truncated[0])"
}

# Same diagnostic grammar the baseline generator parses, so the gate and the inventory can never
# disagree about what counts as a finding.
$diagnosticPattern = '^(?<path>.+?)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning) (?<id>[A-Za-z0-9_]+): (?<message>.*?)(?: \[(?<project>[^\]]+)\])?$'
$changedFileSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$changedFiles, [System.StringComparer]::OrdinalIgnoreCase)
$findingKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$observedCounts = @{}
$observedMessages = @{}

foreach ($line in $formatOutput) {
    $match = [regex]::Match($line, $diagnosticPattern)
    if (-not $match.Success) {
        continue
    }

    $relativePath = ConvertTo-RepositoryRelativePath -Path $match.Groups["path"].Value -Root $repositoryRoot
    if ($null -eq $relativePath -or -not $changedFileSet.Contains($relativePath)) {
        continue
    }

    $diagnosticId = $match.Groups["id"].Value
    # A file owned by more than one project is reported once per owning project; de-duplicate on
    # position so multi-targeted projects cannot inflate the count past the baseline and red the gate.
    $key = "$relativePath|$diagnosticId|$($match.Groups['line'].Value)|$($match.Groups['column'].Value)"
    if (-not $findingKeys.Add($key)) {
        continue
    }

    $bucket = "$relativePath|$diagnosticId"
    if (-not $observedCounts.ContainsKey($bucket)) {
        $observedCounts[$bucket] = 0
        $observedMessages[$bucket] = [System.Collections.Generic.List[string]]::new()
    }

    $observedCounts[$bucket] = $observedCounts[$bucket] + 1
    $observedMessages[$bucket].Add("$relativePath($($match.Groups['line'].Value),$($match.Groups['column'].Value)): $diagnosticId`: $($match.Groups['message'].Value)")
}

$ordinal = [System.StringComparer]::Ordinal
$sortedBuckets = [string[]]@($observedCounts.Keys)
[System.Array]::Sort($sortedBuckets, $ordinal)

$newFindings = [System.Collections.Generic.List[string]]::new()
$trackedFindings = [System.Collections.Generic.List[string]]::new()
$newFindingCount = 0
$trackedFindingCount = 0

foreach ($bucket in $sortedBuckets) {
    $observed = $observedCounts[$bucket]
    $baseline = if ($baselineCounts.ContainsKey($bucket)) { $baselineCounts[$bucket] } else { 0 }
    $tracked = [System.Math]::Min($observed, $baseline)
    $introduced = $observed - $baseline

    $parts = $bucket.Split("|")
    $bucketPath = $parts[0]
    $bucketId = $parts[1]

    if ($tracked -gt 0) {
        $trackedFindingCount += $tracked
        $trackedFindings.Add("  tracked debt: $bucketPath - $bucketId x$tracked (baseline $baseline)")
    }

    if ($introduced -gt 0) {
        $newFindingCount += $introduced
        $newFindings.Add("  NEW: $bucketPath - $bucketId x$introduced (observed $observed, baseline $baseline)")
        # Every observed position is listed. The baseline records counts, not positions, so the
        # gate can prove how many findings are new but never which ones — printing only a "sample"
        # would point at an arbitrary occurrence and send the reader to the wrong line.
        foreach ($observedMessage in $observedMessages[$bucket]) {
            $newFindings.Add("       $observedMessage")
        }
    }
}

if ($trackedFindings.Count -gt 0 -and -not $Quiet) {
    Write-Host "Changed-file formatter gate: $trackedFindingCount pre-existing finding(s) on changed files are recorded in '$BaselinePath' and are reported, not suppressed."
    foreach ($tracked in $trackedFindings) {
        Write-Host $tracked
    }
}

if ($newFindings.Count -gt 0) {
    $message = [System.Collections.Generic.List[string]]::new()
    $message.Add("Changed-file formatter gate FAILED: $newFindingCount formatter finding(s) introduced on files this change touches.")
    foreach ($finding in $newFindings) {
        $message.Add($finding)
    }

    $message.Add("Gated diagnostics: $($gatedDiagnosticIds -join ', '). Fix them with 'dotnet format $SolutionPath --include <file>'.")
    $message.Add("A renamed file inherits no baseline entry by design; repair it, or regenerate eng/format-baseline.json in this change.")
    # [Console]::Error rather than Write-Error: under `pwsh -File` the host renders an error record
    # with ANSI escapes and re-wraps it, which destroys the per-finding line structure this message
    # exists to convey.
    [Console]::Error.WriteLine($message -join [System.Environment]::NewLine)
    exit 1
}

Write-Host "Changed-file formatter gate passed: $($changedFiles.Count) changed C# file(s), 0 new finding(s), $trackedFindingCount tracked baseline finding(s)."
exit 0
