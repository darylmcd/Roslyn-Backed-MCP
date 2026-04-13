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
    if (Test-KeywordAlreadyTracked -Line $Summary -ExistingRows $ExistingRows) {
        return $true
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
        $m = [regex]::Match($line, '^\| `([^`]+)` \| ([^|]*) \| ([^|]*) \| (.*) \|?$')
        if (-not $m.Success) {
            continue
        }
        $id = $m.Groups[1].Value
        if ($id -eq 'id') {
            continue
        }
        $do = $m.Groups[4].Value.Trim()
        if ($do -match '\|-+$') {
            continue
        }
        $rows.Add([pscustomobject]@{ Id = $id; Do = $do })
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
        $newRows.Add([pscustomobject]@{ Id = $id; Priority = $pri; Do = $do })
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
    $pri = Get-PriorityFromDo -Do $r.Do
    $all.Add([pscustomobject]@{ Id = $r.Id; Priority = $pri; Do = $r.Do })
}
foreach ($nr in $newRows) {
    $all.Add($nr)
}

$p2 = $all | Where-Object { $_.Priority -eq 'P2' } | Sort-Object Id
$p3 = $all | Where-Object { $_.Priority -eq 'P3' } | Sort-Object Id
$p4 = $all | Where-Object { $_.Priority -eq 'P4' } | Sort-Object Id
$ordered = @($p2) + @($p3) + @($p4)

$tableRows = foreach ($row in $ordered) {
    "| ``$($row.Id)`` | none | - | $($row.Do) |"
}

$rx = [regex]::new('(?s)(\| id \|[^\r\n]+\r?\n\|[-| ]+\|\r?\n)(.*?)(\r?\n## Refs)', [System.Text.RegularExpressions.RegexOptions]::None)
$m = $rx.Match($backlog)
if (-not $m.Success) {
    throw 'sync-deep-review-backlog: could not find open-work markdown table (expected | id | ... then ## Refs).'
}

$newBody = ($tableRows -join "`n") + "`n"
$backlog = $backlog.Substring(0, $m.Groups[2].Index) + $newBody + $backlog.Substring($m.Groups[2].Index + $m.Groups[2].Length)

Set-Content -LiteralPath $backlogPath -Value $backlog -NoNewline
Write-Host 'sync-deep-review-backlog: updated ai_docs/backlog.md'
