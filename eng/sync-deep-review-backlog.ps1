param(
    [Parameter(Mandatory = $true)]
    [string[]]$CanonicalAuditFiles,

    [string]$RollupPath = '',

    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot
}

function Normalize-ForDedup {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }
    $t = $Text.ToLowerInvariant() -replace '\s+', ' '
    $t = $t -replace '[^a-z0-9\s]', ''
    return $t.Trim()
}

function New-BacklogId {
    param([string]$Summary)
    $s = $Summary -replace '`[^`]*`', ' ' -replace '[^\p{L}\p{Nd}]+', ' '
    $parts = @(
        ($s -split '\s+', [StringSplitOptions]::RemoveEmptyEntries) |
            Where-Object { $_ -notmatch '^\d+$' } |
            Select-Object -First 7
    )
    if ($parts.Count -eq 0) {
        return 'deep-review-candidate'
    }
    $slug = ($parts -join '-').ToLowerInvariant()
    $slug = [regex]::Replace($slug, '[^a-z0-9-]+', '-')
    $slug = [regex]::Replace($slug, '-+', '-').Trim('-')
    if ($slug.Length -gt 52) {
        $slug = $slug.Substring(0, 52).TrimEnd('-')
    }
    if ($slug.Length -lt 8) {
        $slug = "deep-review-$slug"
    }
    if ($slug -match '^\d') {
        $slug = "dr-$slug"
    }
    return $slug
}

function Get-PriorityLabel {
    param([string]$Line)
    $l = $Line.ToLowerInvariant()
    if ($l -match '\bfail\b|corrupt|data loss|p2\b') {
        return 'P2'
    }
    if ($l -match 'p3\b|test_run|workspace_list|semantic_search') {
        return 'P3'
    }
    return 'P4'
}

function Get-AreaTag {
    param([string]$Priority)
    switch ($Priority) {
        'P2' { return 'reliability' }
        'P3' { return 'product' }
        default { return 'tracking' }
    }
}

function Test-KeywordAlreadyTracked {
    param(
        [string]$Line,
        [array]$ExistingRows
    )
    $tools = @(
        'semantic_search', 'project_diagnostics', 'test_run',
        'find_references_bulk', 'apply_text_edit', 'set_editorconfig_option',
        'workspace_list', 'move_file_preview',
        'scaffold_type_preview', 'scaffold_test_preview',
        'revert_last_apply', 'dependency_inversion_preview',
        'extract_interface_preview', 'extract_and_wire_interface_preview',
        'migrate_package_preview', 'add_central_package_version_preview',
        'move_type_to_project_preview', 'rename_preview',
        'server_info', 'fix_all_preview', 'compile_check',
        'extract_method_preview', 'split_class_preview',
        'add_target_framework_preview', 'remove_target_framework_preview',
        'find_base_members', 'find_overrides', 'test_coverage',
        'test_discover', 'test_related', 'symbol_search'
    )
    foreach ($t in $tools) {
        if ($Line -notmatch [regex]::Escape($t)) {
            continue
        }
        foreach ($row in $ExistingRows) {
            if ($row.Do -match [regex]::Escape($t)) {
                return $true
            }
        }
    }
    return $false
}

function Test-IsDuplicateOfExisting {
    param(
        [string]$Summary,
        [array]$ExistingRows
    )
    # Do not treat MCP issue headings (`### 9.x …`) as duplicates just because they name a tool
    # that already appears in another row's prose — otherwise unrelated findings (e.g. §9.5 vs
    # `compile-check-stale-assembly-refs-post-reload`) are dropped and never reach the backlog.
    if ($Summary -notmatch '^\s*###\s+\d+\.\d+\s+') {
        if (Test-KeywordAlreadyTracked -Line $Summary -ExistingRows $ExistingRows) {
            return $true
        }
    }
    $n = Normalize-ForDedup -Text $Summary
    if ($n.Length -lt 24) {
        return $true
    }
    $prefix = if ($n.Length -ge 48) {
        $n.Substring(0, 48)
    }
    else {
        $n
    }
    foreach ($row in $ExistingRows) {
        $e = Normalize-ForDedup -Text $row.Do
        if ($e.Length -lt 20) {
            continue
        }
        if ($e.Contains($prefix)) {
            return $true
        }
        $ep = $e.Substring(0, [Math]::Min(48, $e.Length))
        if ($n.Contains($ep)) {
            return $true
        }
    }
    return $false
}

function Get-CandidatesFromMarkdown {
    param([string]$FilePath)

    $lines = Get-Content -LiteralPath $FilePath
    $candidates = New-Object System.Collections.Generic.List[string]
    $mode = 'none'

    foreach ($line in $lines) {
        if ($line -match '^##\s+\d+\.\s+MCP server issues') {
            $mode = 'bugs'
            continue
        }
        if ($line -match '^##\s+') {
            $mode = 'none'
        }

        if ($mode -eq 'bugs') {
            if ($line -match '\*\*Backlog:\*\*\s*`') {
                continue
            }
            if ($line -match '^###\s+\d+\.\d+\s+' -and $line.Length -gt 30) {
                $candidates.Add($line.Trim())
            }
            elseif ($line -match '^\|\s*[^|]+\|\s*[^|]*\b(FAIL|FLAG)\b' -and $line.Length -gt 45) {
                $candidates.Add($line.Trim())
            }
            elseif ($line -match '^\s*\d+\.\s+' -and $line.Length -gt 45 -and ($line -cmatch '\bFAIL\b|\bFLAG\b' -or $line -match 'corrupt diff|MCP bridge|invocation failed')) {
                $candidates.Add($line.Trim())
            }
        }
    }

    return $candidates
}

function Parse-BacklogRows {
    param([string]$Content)

    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($line in ($Content -split "`r?`n")) {
        $m5 = [regex]::Match($line, '^\| `([^`]+)` \| (P[234]) \| ([^|]*) \| ([^|]*) \| (.*) \|?\s*$')
        if ($m5.Success) {
            $id = $m5.Groups[1].Value.Trim()
            if ($id -eq 'id') {
                continue
            }
            $do = $m5.Groups[5].Value.Trim()
            if ($do -match '\|-+$') {
                continue
            }
            $rows.Add([pscustomobject]@{
                    Id       = $id
                    Priority = $m5.Groups[2].Value.Trim()
                    Blocker  = $m5.Groups[3].Value.Trim()
                    Deps     = $m5.Groups[4].Value.Trim()
                    Do       = $do
                })
            continue
        }

        # Legacy: | id | blocker | deps | do |
        $m4 = [regex]::Match($line, '^\| `([^`]+)` \| ([^|]*) \| ([^|]*) \| (.*) \|?\s*$')
        if (-not $m4.Success) {
            continue
        }
        $id = $m4.Groups[1].Value.Trim()
        if ($id -eq 'id') {
            continue
        }
        $do = $m4.Groups[4].Value.Trim()
        if ($do -match '\|-+$') {
            continue
        }
        $pri = Get-PriorityFromDo -Do $do
        $rows.Add([pscustomobject]@{
                Id       = $id
                Priority = $pri
                Blocker  = $m4.Groups[2].Value.Trim()
                Deps     = $m4.Groups[3].Value.Trim()
                Do       = $do
            })
    }
    return $rows
}

function Get-PriorityFromDo {
    param([string]$Do)
    $m = [regex]::Match($Do, '\*\*(P[234])\b')
    if ($m.Success) {
        return $m.Groups[1].Value
    }
    return 'P4'
}

function Format-OpenWorkSection {
    param(
        [string]$Heading,
        [string]$WithinLine,
        [object[]]$Rows
    )

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine($Heading)
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($WithinLine)
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| id | pri | blocker | deps | do |')
    [void]$sb.AppendLine('|----|-----|---------|------|-----|')
    foreach ($row in $Rows) {
        $b = if ([string]::IsNullOrWhiteSpace($row.Blocker)) { '—' } else { $row.Blocker.Trim() }
        $d = if ([string]::IsNullOrWhiteSpace($row.Deps)) { '—' } else { $row.Deps.Trim() }
        [void]$sb.AppendLine("| ``$($row.Id)`` | $($row.Priority) | $b | $d | $($row.Do) |")
    }
    return $sb.ToString()
}

$repoRoot = Get-RepoRoot
$CanonicalAuditFiles = @($CanonicalAuditFiles)

$backlogPath = Join-Path $repoRoot 'ai_docs/backlog.md'
$backlog = Get-Content -LiteralPath $backlogPath -Raw
$existing = @(Parse-BacklogRows -Content $backlog)
$existingIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($r in $existing) {
    [void]$existingIds.Add($r.Id)
}

$newRows = New-Object System.Collections.Generic.List[object]
$seenNewFingerprints = New-Object System.Collections.Generic.HashSet[string]

foreach ($audit in $CanonicalAuditFiles) {
    if (-not (Test-Path -LiteralPath $audit)) {
        continue
    }
    $fn = [System.IO.Path]::GetFileName($audit)
    foreach ($c in (Get-CandidatesFromMarkdown -FilePath $audit)) {
        if (Test-IsDuplicateOfExisting -Summary $c -ExistingRows $existing) {
            continue
        }
        $summary = "**Auto (deep-review).** $fn - $c"
        if ($summary.Length -gt 1200) {
            $summary = $summary.Substring(0, 1197) + '...'
        }
        $fp = (Normalize-ForDedup -Text $c)
        if ($fp.Length -lt 20) {
            continue
        }
        if ($seenNewFingerprints.Contains($fp)) {
            continue
        }
        [void]$seenNewFingerprints.Add($fp)

        $pri = Get-PriorityLabel -Line $c
        $area = Get-AreaTag -Priority $pri
        $baseId = New-BacklogId -Summary $c
        $id = $baseId
        $n = 2
        while ($existingIds.Contains($id)) {
            $id = "$baseId-$n"
            $n++
        }
        [void]$existingIds.Add($id)

        $do = "**$pri / $area.** $summary"
        $newRows.Add([pscustomobject]@{
                Id       = $id
                Priority = $pri
                Blocker  = '—'
                Deps     = '—'
                Do       = $do
            })
    }
}

if ($newRows.Count -eq 0) {
    Write-Host 'sync-deep-review-backlog: no new issue rows to add (deduped against existing table).'
    return
}

Write-Host "sync-deep-review-backlog: $($newRows.Count) new row(s) to merge."
foreach ($nr in $newRows) {
    Write-Host "  + $($nr.Id) [$($nr.Priority)]"
}

if ($WhatIf) {
    Write-Host 'sync-deep-review-backlog: WhatIf - no changes written.'
    return
}

$utc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$backlog = $backlog -replace '(\*\*updated_at:\*\*)\s*[^\r\n]+', "`$1 $utc"

$all = New-Object System.Collections.Generic.List[object]
foreach ($r in $existing) {
    $all.Add($r)
}
foreach ($nr in $newRows) {
    $all.Add($nr)
}

$p2 = @($all | Where-Object { $_.Priority -eq 'P2' } | Sort-Object Id)
$p3 = @($all | Where-Object { $_.Priority -eq 'P3' } | Sort-Object Id)
$p4 = @($all | Where-Object { $_.Priority -eq 'P4' } | Sort-Object Id)

$p2Block = ''
if ($p2.Count -gt 0) {
    $p2Block = (Format-OpenWorkSection -Heading '## P2 — open work' -WithinLine 'Within P2, rows are **alphabetical by `id`**.' -Rows $p2) + "`n"
}

$p3Block = Format-OpenWorkSection -Heading '## P3 — open work' -WithinLine 'Within P3, rows are **alphabetical by `id`**.' -Rows $p3
$p4Block = Format-OpenWorkSection -Heading '## P4 — open work' -WithinLine 'Within P4, rows are **alphabetical by `id`**.' -Rows $p4
$newOpenWork = ($p2Block + $p3Block + "`n" + $p4Block).TrimEnd()

$endMarker = [regex]::new('\r?\n## Evidence and paths|\r?\n## Refs')
$mEnd = $endMarker.Match($backlog)
if (-not $mEnd.Success) {
    throw 'sync-deep-review-backlog: could not find ## Evidence and paths or ## Refs (expected after P4 open-work table).'
}
$startP2 = $backlog.IndexOf('## P2 — open work', [StringComparison]::Ordinal)
$startP3 = $backlog.IndexOf('## P3 — open work', [StringComparison]::Ordinal)
$startOpen = $backlog.IndexOf('## Open work', [StringComparison]::Ordinal)
if ($startP2 -lt 0 -and $startP3 -lt 0 -and $startOpen -lt 0) {
    throw 'sync-deep-review-backlog: could not find ## P2 — open work, ## P3 — open work, or ## Open work.'
}
$start = if ($startP2 -ge 0) { $startP2 } elseif ($startP3 -ge 0) { $startP3 } else { $startOpen }

$backlog = $backlog.Substring(0, $start) + $newOpenWork + "`r`n`r`n" + $backlog.Substring($mEnd.Index)

Set-Content -LiteralPath $backlogPath -Value $backlog -NoNewline
Write-Host 'sync-deep-review-backlog: updated ai_docs/backlog.md'
