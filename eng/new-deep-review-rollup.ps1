param(
    [Parameter(Mandatory = $true)]
    [string[]]$AuditFiles,

    [string]$OutputPath,

    [string]$CampaignPurpose = 'Deep-review rollup'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    return Split-Path -Parent $PSScriptRoot
}

function Resolve-InputPath {
    param(
        [string]$Path,
        [string]$RepoRoot
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

function Get-HeaderValue {
    param(
        [string[]]$Lines,
        [string]$Name
    )

    $pattern = '^- \*\*' + [regex]::Escape($Name) + ':\*\*\s*(.*)$'
    foreach ($line in $Lines) {
        $match = [regex]::Match($line, $pattern)
        if ($match.Success) {
            return $match.Groups[1].Value.Trim()
        }
    }

    return ''
}

function Get-RepoIdFromFileName {
    param([string]$FileName)

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    $match = [regex]::Match($baseName, '^\d{8}T\d{6}Z_(.+)_mcp-server-audit$')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return $baseName
}

function New-RollupMarkdown {
    param(
        [string]$GeneratedAt,
        [string]$Purpose,
        [string]$OutputFile,
        [System.Collections.Generic.List[object]]$Rows
    )

    $inputRows = foreach ($row in $Rows) {
        '| {0} | {1} | {2} | {3} | {4} | {5} |' -f
            $row.RepoId,
            $row.Date,
            $row.Client,
            $row.Revision,
            $row.Server,
            $row.RelativePath
    }

    if (-not $inputRows) {
        $inputRows = '| | | | | | |'
    }

    @"
# Deep-review rollup

## Scope
- **Generated:** $GeneratedAt
- **Purpose:** $Purpose
- **Input audit count:** $($Rows.Count)
- **Output path:** $OutputFile

## Input audits
| Repo | Date | Client | Revision | Server | File |
|------|------|--------|----------|--------|------|
$($inputRows -join [Environment]::NewLine)

## Repo matrix coverage
| Bucket | Covered | Evidence | Notes |
|--------|---------|----------|-------|
| Small or single-project repo | | | |
| Multi-project repo with tests | | | |
| DI-heavy repo | | | |
| Source-generator repo | | | |
| Central Package Management or multi-targeting repo | | | |
| Large solution representative repo | | | |

## Client coverage
| Client | Full-surface | Notes |
|--------|--------------|-------|
| | | |

## Deduped issues
| Key | Severity | Evidence | Backlog action |
|-----|----------|----------|----------------|
| | | | |

## Blocked-by-client summary
- 

## Candidate closures
| Source id | Evidence | Notes |
|-----------|----------|-------|
| | | |

## Backlog actions
- 
"@
}

$repoRoot = Get-RepoRoot
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "ai_docs/reports/${timestamp}_deep-review-rollup.md"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$rows = New-Object 'System.Collections.Generic.List[object]'

foreach ($auditFile in $AuditFiles) {
    $resolvedPath = Resolve-InputPath -Path $auditFile -RepoRoot $repoRoot
    if (-not (Test-Path $resolvedPath)) {
        throw "Audit file not found: $auditFile"
    }

    $lines = Get-Content -Path $resolvedPath
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $resolvedPath).Replace('\\', '/')

    $rows.Add([pscustomobject]@{
            RepoId = Get-RepoIdFromFileName -FileName $resolvedPath
            Date = Get-HeaderValue -Lines $lines -Name 'Date'
            Client = Get-HeaderValue -Lines $lines -Name 'Client'
            Revision = Get-HeaderValue -Lines $lines -Name 'Audited revision'
            Server = Get-HeaderValue -Lines $lines -Name 'Server'
            RelativePath = $relativePath
        })
}

$markdown = New-RollupMarkdown -GeneratedAt $timestamp -Purpose $CampaignPurpose -OutputFile ([System.IO.Path]::GetRelativePath($repoRoot, $OutputPath).Replace('\\', '/')) -Rows $rows
Set-Content -Path $OutputPath -Value $markdown -NoNewline

Write-Host "Created rollup scaffold: $OutputPath"