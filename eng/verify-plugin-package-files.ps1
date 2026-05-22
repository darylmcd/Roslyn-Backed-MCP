param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AllowlistPath = (Join-Path (Split-Path -Parent $PSScriptRoot) ".claude-plugin/package-allowlist.txt"),
    [string]$CandidateFileListPath
)

$ErrorActionPreference = "Stop"

$forbiddenPrefixes = @(
    "ai_docs/",
    "src/",
    "tests/",
    "samples/",
    "analyzers/",
    "eng/",
    ".claude/",
    ".github/",
    ".cursor/",
    ".vscode/",
    "review-inbox/",
    "changelog.d/",
    "artifacts/",
    ".worktrees/"
)

$forbiddenExact = @(
    "Directory.Build.props",
    "Directory.Build.rsp",
    "Directory.Packages.props",
    "BannedSymbols.txt",
    "RoslynMcp.slnx",
    "justfile"
)

function Normalize-PackagePath([string]$Path) {
    $normalized = $Path.Trim().Replace('\', '/')
    while ($normalized.StartsWith("./", [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    return $normalized
}

function Read-Allowlist([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Plugin package allowlist not found: $Path"
    }

    $patterns = [System.Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#", [StringComparison]::Ordinal)) {
            continue
        }

        $normalized = Normalize-PackagePath $trimmed
        if ([System.IO.Path]::IsPathRooted($normalized) -or $normalized.Contains("..")) {
            throw "Allowlist entry '$trimmed' must be repo-relative and must not contain '..'."
        }

        foreach ($prefix in $forbiddenPrefixes) {
            if ($normalized.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Allowlist entry '$trimmed' points at repo-internal path '$prefix'."
            }
        }

        foreach ($exact in $forbiddenExact) {
            if ($normalized.Equals($exact, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Allowlist entry '$trimmed' points at release/build internal file '$exact'."
            }
        }

        $patterns.Add($normalized)
    }

    if ($patterns.Count -eq 0) {
        throw "Plugin package allowlist is empty: $Path"
    }

    return $patterns.ToArray()
}

function Test-AllowlistMatch([string]$Path, [string[]]$Patterns) {
    $normalized = Normalize-PackagePath $Path
    foreach ($pattern in $Patterns) {
        if ($pattern.EndsWith("/**", [StringComparison]::Ordinal)) {
            $prefix = $pattern.Substring(0, $pattern.Length - 2)
            if ($normalized.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        } elseif ($pattern.Contains("*")) {
            if ($normalized -like $pattern) {
                return $true
            }
        } elseif ($normalized.Equals($pattern, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Get-GitTrackedFiles([string]$Root) {
    $output = & git -C $Root ls-files
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed with exit code $LASTEXITCODE."
    }
    return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$allowlist = Read-Allowlist $AllowlistPath
$trackedFiles = Get-GitTrackedFiles $RepoRoot

$missingRequired = [System.Collections.Generic.List[string]]::new()
foreach ($pattern in $allowlist) {
    if ($pattern.Contains("*")) {
        if (-not ($trackedFiles | Where-Object { Test-AllowlistMatch $_ @($pattern) } | Select-Object -First 1)) {
            $missingRequired.Add($pattern)
        }
        continue
    }

    $candidate = Join-Path $RepoRoot $pattern
    if (-not (Test-Path $candidate)) {
        $missingRequired.Add($pattern)
    }
}

if ($missingRequired.Count -gt 0) {
    throw "Plugin package allowlist required file(s) missing: $($missingRequired -join ', ')"
}

if ($CandidateFileListPath) {
    if (-not (Test-Path $CandidateFileListPath)) {
        throw "Candidate file list not found: $CandidateFileListPath"
    }
    $candidateFiles = @(Get-Content $CandidateFileListPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
} else {
    $candidateFiles = @($trackedFiles | Where-Object { Test-AllowlistMatch $_ $allowlist })
}

$unexpected = @($candidateFiles | Where-Object { -not (Test-AllowlistMatch $_ $allowlist) })
if ($unexpected.Count -gt 0) {
    Write-Error ("Plugin package contains non-allowlisted file(s):`n" + ($unexpected | Sort-Object | ForEach-Object { "  - $_" } | Out-String))
    exit 1
}

Write-Host "Plugin package allowlist verified: $($allowlist.Count) allowlist entries, $($candidateFiles.Count) candidate files."
