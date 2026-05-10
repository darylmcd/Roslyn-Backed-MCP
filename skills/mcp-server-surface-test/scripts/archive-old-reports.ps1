<#
.SYNOPSIS
    Archive old audit reports under audit-reports/ into year-stamped subdirectories.

.DESCRIPTION
    Moves *.md files in <RepoRoot>/<ReportsRelativePath>/ that are older than -OlderThanDays days
    into <RepoRoot>/<ReportsRelativePath>/archive/<YYYY>/, where <YYYY> is the file's
    LastWriteTime year. Files already inside the `archive/` subtree are left untouched.

    README.md is pinned by name and is never archived regardless of age.

    The script is idempotent: running it twice is safe. The destination year-subdirectory is
    created on demand. If a destination file with the same name already exists, the move is
    skipped and a warning is emitted (the operator can resolve the collision manually).

.PARAMETER RepoRoot
    Repository root path. Defaults to the directory three levels above this script
    (skills/mcp-server-surface-test/scripts/ -> skills/mcp-server-surface-test/ -> skills/ -> repo root).

.PARAMETER ReportsRelativePath
    Path (relative to RepoRoot) of the audit-reports directory to archive. Defaults to
    the /mcp-server-surface-test convention. Override when the host repo writes
    audit drafts to a different relative path.

.PARAMETER OlderThanDays
    Minimum age in days for a report to be archived. Defaults to 30. Files with
    LastWriteTime newer than (now - OlderThanDays) are left in place.

.PARAMETER DryRun
    When set, the script reports what it would do but performs no filesystem mutations.
    Use this in tests and to preview the archive plan before committing.

.EXAMPLE
    pwsh -NoProfile -File skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -DryRun
        Reports which files would be archived without moving anything.

.EXAMPLE
    pwsh -NoProfile -File skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -OlderThanDays 60
        Archives reports older than 60 days into <ReportsRelativePath>/archive/<YYYY>/.

.NOTES
    Designed to be invoked from PowerShell 7+ (pwsh) on Windows or Linux/macOS via
    `pwsh -NoProfile -File <path>`. The script does not require the Roslyn MCP server.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,

    [string]$ReportsRelativePath = 'audit-reports',

    [ValidateRange(0, 36500)]
    [int]$OlderThanDays = 30,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    # script -> skills/audit-deep/scripts/ -> skills/audit-deep/ -> skills/ -> repo root
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).ProviderPath
}

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "RepoRoot '$RepoRoot' does not exist or is not a directory."
}

$reportsDir = Join-Path $RepoRoot $ReportsRelativePath
if (-not (Test-Path -LiteralPath $reportsDir -PathType Container)) {
    Write-Output "audit-reports directory not present at '$reportsDir' — nothing to archive."
    return
}

$archiveRoot = Join-Path $reportsDir 'archive'
$cutoff = (Get-Date).AddDays(-1 * $OlderThanDays)

# Files pinned by name — never archived even if old.
$pinnedNames = @('README.md')

$candidates = Get-ChildItem -LiteralPath $reportsDir -Filter '*.md' -File |
    Where-Object {
        # Skip pinned filenames.
        if ($pinnedNames -contains $_.Name) { return $false }
        # Skip anything already under the archive/ subtree (Get-ChildItem with no -Recurse
        # won't enter it anyway, but belt-and-suspenders against future call-site changes).
        if ($_.FullName.StartsWith($archiveRoot, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
        return $_.LastWriteTime -lt $cutoff
    }

$summary = [ordered]@{
    repoRoot       = $RepoRoot
    reportsDir     = $reportsDir
    archiveRoot    = $archiveRoot
    olderThanDays  = $OlderThanDays
    cutoff         = $cutoff.ToString('o')
    dryRun         = [bool]$DryRun
    candidateCount = $candidates.Count
    moved          = New-Object System.Collections.Generic.List[string]
    skipped        = New-Object System.Collections.Generic.List[string]
}

if ($candidates.Count -eq 0) {
    Write-Output "No reports older than $OlderThanDays days under '$reportsDir'."
    return [pscustomobject]$summary
}

foreach ($file in $candidates) {
    $year = $file.LastWriteTime.Year.ToString('D4')
    $destDir = Join-Path $archiveRoot $year
    $destPath = Join-Path $destDir $file.Name

    if (Test-Path -LiteralPath $destPath) {
        $msg = "SKIP '$($file.Name)' -> '$destPath' (destination already exists)"
        Write-Warning $msg
        $summary.skipped.Add($msg)
        continue
    }

    if ($DryRun) {
        Write-Output "DRY-RUN move: $($file.FullName) -> $destPath"
        $summary.moved.Add("$($file.FullName) -> $destPath")
        continue
    }

    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    Move-Item -LiteralPath $file.FullName -Destination $destPath
    Write-Output "Moved: $($file.FullName) -> $destPath"
    $summary.moved.Add("$($file.FullName) -> $destPath")
}

Write-Output ""
Write-Output "Archive summary: candidates=$($summary.candidateCount), moved=$($summary.moved.Count), skipped=$($summary.skipped.Count), dryRun=$($summary.dryRun)"

return [pscustomobject]$summary
