Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

param(
    [Parameter(Mandatory = $true)]
    [string[]]$AuditFiles,

    [string]$OutputPath,

    [string]$CampaignPurpose = 'Deep-review rollup',

    [switch]$Force
)

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

$repoRoot = Get-RepoRoot
$importScript = Join-Path $PSScriptRoot 'import-deep-review-audit.ps1'
$rollupScript = Join-Path $PSScriptRoot 'new-deep-review-rollup.ps1'

$resolvedAuditFiles = foreach ($auditFile in $AuditFiles) {
    Resolve-InputPath -Path $auditFile
}

& $importScript -AuditFiles $resolvedAuditFiles -Force:$Force

$canonicalAuditFiles = foreach ($resolvedAuditFile in $resolvedAuditFiles) {
    Join-Path $repoRoot ('ai_docs/audit-reports/' + [System.IO.Path]::GetFileName($resolvedAuditFile))
}

& $rollupScript -AuditFiles $canonicalAuditFiles -OutputPath $OutputPath -CampaignPurpose $CampaignPurpose