param(
    [string[]]$AuditFiles = @(),

    [string]$SiblingRepoParent = '',

    [string[]]$ExcludeRepoFolders = @(),

    [string]$OutputPath,

    [string]$CampaignPurpose = 'Deep-review rollup',

    [switch]$NoOverwrite,

    [switch]$SkipBacklogSync,

    [switch]$BacklogWhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot
}

function Resolve-InputPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Get-RepoIdFromAuditFileName {
    param([string]$FileName)

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    $match = [regex]::Match($baseName, '^\d{8}T\d{6}Z_(.+)_(mcp-server-audit|experimental-promotion)$')
    if ($match.Success) {
        return $match.Groups[1].Value.ToLowerInvariant()
    }

    return ''
}

function Get-ReportType {
    param([string]$FileName)

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    if ($baseName -match '_experimental-promotion$') { return 'experimental-promotion' }
    if ($baseName -match '_mcp-server-audit$') { return 'mcp-server-audit' }
    return 'unknown'
}

function Get-AuditTimestampPrefix {
    param([string]$FileName)

    $match = [regex]::Match($FileName, '^(\d{8}T\d{6}Z)_')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return ''
}

$repoRoot = Get-RepoRoot
$importScript = Join-Path $PSScriptRoot 'import-deep-review-audit.ps1'
$rollupScript = Join-Path $PSScriptRoot 'new-deep-review-rollup.ps1'
$syncBacklogScript = Join-Path $PSScriptRoot 'sync-deep-review-backlog.ps1'

$selfFolderName = Split-Path -Leaf $repoRoot
if ($ExcludeRepoFolders.Count -eq 0) {
    $ExcludeRepoFolders = @($selfFolderName)
}

# --- Resolve audit file list: explicit paths or auto-discover from sibling repos + this repo ---
$discoveredFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]

if ($AuditFiles.Count -eq 0) {
    $parent = if (-not [string]::IsNullOrWhiteSpace($SiblingRepoParent)) {
        [System.IO.Path]::GetFullPath($SiblingRepoParent)
    }
    else {
        Split-Path -Parent $repoRoot
    }

    if (-not (Test-Path -LiteralPath $parent)) {
        throw "Sibling repo parent not found: $parent. Set -SiblingRepoParent or pass -AuditFiles explicitly."
    }

    foreach ($dir in (Get-ChildItem -LiteralPath $parent -Directory -ErrorAction Stop)) {
        if ($ExcludeRepoFolders -contains $dir.Name) {
            continue
        }
        $auditDir = Join-Path $dir.FullName 'ai_docs/audit-reports'
        if (-not (Test-Path -LiteralPath $auditDir)) {
            continue
        }
        foreach ($pattern in @('*_mcp-server-audit.md', '*_experimental-promotion.md')) {
            Get-ChildItem -LiteralPath $auditDir -Filter $pattern -File -ErrorAction SilentlyContinue |
                ForEach-Object { $discoveredFiles.Add($_) }
        }
    }

    $localAuditDir = Join-Path $repoRoot 'ai_docs/audit-reports'
    if (Test-Path -LiteralPath $localAuditDir) {
        foreach ($pattern in @('*_mcp-server-audit.md', '*_experimental-promotion.md')) {
            Get-ChildItem -LiteralPath $localAuditDir -Filter $pattern -File -ErrorAction SilentlyContinue |
                ForEach-Object { $discoveredFiles.Add($_) }
        }
    }

    if ($discoveredFiles.Count -eq 0) {
        throw "No audit or experimental-promotion files found under sibling repos in '$parent' or under '$localAuditDir'. Run deep-review in target repos first, or pass -AuditFiles."
    }

    $byRepoType = @{}
    foreach ($f in $discoveredFiles) {
        $rid = Get-RepoIdFromAuditFileName -FileName $f.Name
        if ([string]::IsNullOrWhiteSpace($rid)) {
            continue
        }
        $rtype = Get-ReportType -FileName $f.Name
        $key = "${rid}_${rtype}"
        $ts = Get-AuditTimestampPrefix -FileName $f.Name
        if (-not $byRepoType.ContainsKey($key) -or ($ts -gt (Get-AuditTimestampPrefix -FileName $byRepoType[$key].Name))) {
            $byRepoType[$key] = $f
        }
    }

    $resolvedAuditFiles = foreach ($k in ($byRepoType.Keys | Sort-Object)) {
        $byRepoType[$k].FullName
    }

    Write-Host "new-deep-review-batch: auto-discovered $($resolvedAuditFiles.Count) report(s) (latest per repo-id + report-type under '$parent')."
}
else {
    $resolvedAuditFiles = foreach ($auditFile in $AuditFiles) {
        Resolve-InputPath -Path $auditFile
    }
}

$importForce = -not $NoOverwrite
& $importScript -AuditFiles $resolvedAuditFiles -Force:$importForce

$canonicalAuditFiles = foreach ($resolvedAuditFile in $resolvedAuditFiles) {
    Join-Path $repoRoot ('ai_docs/audit-reports/' + [System.IO.Path]::GetFileName($resolvedAuditFile))
}

$rollupRelative = $OutputPath
if ([string]::IsNullOrWhiteSpace($rollupRelative)) {
    $ts = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $rollupRelative = "ai_docs/reports/${ts}_deep-review-rollup.md"
}

& $rollupScript -AuditFiles $canonicalAuditFiles -OutputPath $rollupRelative -CampaignPurpose $CampaignPurpose

$rollupOut = if ([System.IO.Path]::IsPathRooted($rollupRelative)) {
    [System.IO.Path]::GetFullPath($rollupRelative)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $rollupRelative))
}
Write-Host "new-deep-review-batch: rollup path: $rollupOut"

if (-not $SkipBacklogSync) {
    & $syncBacklogScript -CanonicalAuditFiles $canonicalAuditFiles -RollupPath $rollupOut -WhatIf:$BacklogWhatIf
}
else {
    Write-Host 'new-deep-review-batch: skipped backlog sync (-SkipBacklogSync).'
}
