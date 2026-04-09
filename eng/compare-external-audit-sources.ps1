param(
    [Parameter(Mandatory = $true)]
    [string[]]$ExternalRepoRoot,

    [switch]$FailOnDrift
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot
}

$repoRoot = Get-RepoRoot
$canonicalDir = Join-Path $repoRoot 'ai_docs/audit-reports'

if (-not (Test-Path $canonicalDir)) {
    throw "Canonical audit directory not found: $canonicalDir"
}

$drifts = New-Object System.Collections.Generic.List[string]

foreach ($rootRaw in $ExternalRepoRoot) {
    $root = [System.IO.Path]::GetFullPath($rootRaw)
    $auditDir = Join-Path $root 'ai_docs/audit-reports'
    if (-not (Test-Path $auditDir)) {
        Write-Warning "compare-external-audit-sources: no audit directory at $auditDir (repo root: $root)"
        continue
    }

    Get-ChildItem -Path $auditDir -Filter '*_mcp-server-audit.md' -File | ForEach-Object {
        $ext = $_
        $destPath = Join-Path $canonicalDir $ext.Name
        if (-not (Test-Path $destPath)) {
            $drifts.Add("MISSING in canonical store: $($ext.Name) — import from: $($ext.FullName)")
            return
        }

        $can = Get-Item -LiteralPath $destPath
        $newer = $ext.LastWriteTimeUtc -gt $can.LastWriteTimeUtc.AddSeconds(3)
        $differentSize = $ext.Length -ne $can.Length
        if ($newer -or $differentSize) {
            $extWhen = $ext.LastWriteTimeUtc.ToString('o')
            $canWhen = $can.LastWriteTimeUtc.ToString('o')
            $drifts.Add(
                "STALE or DIFFER: $($ext.Name) — external newer or size differs (ext $($ext.Length) @ $extWhen vs can $($can.Length) @ $canWhen). Run: ./eng/import-deep-review-audit.ps1 -AuditFiles '$($ext.FullName)' -Force"
            )
        }
    }
}

foreach ($line in $drifts) {
    Write-Warning $line
}

if ($drifts.Count -gt 0 -and $FailOnDrift) {
    throw "compare-external-audit-sources: $($drifts.Count) drift issue(s). Import external files into ai_docs/audit-reports/ before continuing."
}

if ($drifts.Count -eq 0) {
    Write-Host 'compare-external-audit-sources: no missing or stale canonical audits for scanned external roots.'
}
