param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$stalePatterns = @(
    'agent_session_instructions.md',
    'editor_notes.md',
    'open_and_deferred_items.md',
    'ai_docs/tmp',
    'ai_docs/quickstart.md'
)

# Enumerate only files git considers part of the project (tracked + untracked-but-not-ignored).
# Skips .gitignore'd paths (.claude/ user state, artifacts/, bin/, obj/, etc.) so doc checks
# only validate files that will actually ship.
$gitFilesRaw = & git -C $RepoRoot ls-files --cached --others --exclude-standard 2>$null
if ($LASTEXITCODE -ne 0 -or -not $gitFilesRaw) {
    Write-Error "Unable to enumerate files via 'git ls-files'. Is this a git checkout?"
    exit 1
}

$allFiles = foreach ($relative in $gitFilesRaw) {
    $fullPath = Join-Path $RepoRoot $relative
    # Test-Path + Get-Item is not atomic and trips $ErrorActionPreference='Stop'
    # when Get-Item sees a stale read — e.g. a tracked file that the CI runner's
    # checkout hasn't written yet. Use a BCL existence probe + FileInfo, both
    # synchronous and immune to the PowerShell pipeline's resume semantics.
    if ([System.IO.File]::Exists($fullPath)) {
        [System.IO.FileInfo]::new($fullPath)
    }
}

$staleSearchExtensions = @('.md', '.ps1', '.yml', '.yaml', '.json')
$contentFiles = $allFiles | Where-Object {
    $staleSearchExtensions -contains $_.Extension -and $_.FullName -ne $PSCommandPath
}
$markdownFiles = $allFiles | Where-Object { $_.Extension -eq '.md' }

$issues = New-Object System.Collections.Generic.List[string]

foreach ($pattern in $stalePatterns) {
    $matches = Select-String -Path $contentFiles.FullName -Pattern $pattern -SimpleMatch
    foreach ($match in $matches) {
        $issues.Add("Stale reference: $($match.Path):$($match.LineNumber) -> $pattern")
    }
}

foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $matches = [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')

    foreach ($match in $matches) {
        $target = $match.Groups[1].Value.Trim()

        if ($target -match '^(https?:|mailto:|#)') {
            continue
        }

        # Skip template placeholder links like ([#{prNumber}]({prUrl})) — doc authors
        # use these in example blocks; they are not resolvable filesystem paths.
        if ($target -match '\{[^}]+\}') {
            continue
        }

        $pathPart = $target.Split('#')[0].Split('?')[0] -replace '%20', ' '
        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            continue
        }

        # All-dot targets like `(...)` are ellipsis placeholders, never real paths. Windows
        # path normalization makes Test-Path resolve them (so the self-hosted PR runs passed)
        # while Linux treats them as a plain missing filename (so every hosted-ubuntu run
        # failed) — flag them on every platform so PR CI catches what the weekly run would.
        # Intentional placeholder links must use the {braces} convention skipped above.
        if ($pathPart -match '^\.{3,}[/\\]?$') {
            $issues.Add("Broken relative link (all-dot placeholder; use {braces} instead): $($file.FullName) -> $target")
            continue
        }

        if ($pathPart -match '^[a-zA-Z]:\\') {
            if (-not (Test-Path -LiteralPath $pathPart)) {
                $issues.Add("Broken absolute link: $($file.FullName) -> $target")
            }

            continue
        }

        $resolved = Join-Path -Path $file.DirectoryName -ChildPath $pathPart
        if (-not (Test-Path -LiteralPath $resolved)) {
            $issues.Add("Broken relative link: $($file.FullName) -> $target")
        }
    }
}

try {
    & (Join-Path $PSScriptRoot 'verify-upgrade-matrix.ps1') -RepoRoot $RepoRoot
}
catch {
    $issues.Add("Upgrade matrix parity: $($_.Exception.Message)")
}

try {
    & (Join-Path $PSScriptRoot 'update-third-party-notices.ps1') -RepoRoot $RepoRoot -Verify
}
catch {
    $issues.Add("Third-party notice parity: $($_.Exception.Message)")
}

$toolSurfaceDecisionRelativePath = 'docs/decisions/0009-tool-surface-policy.md'
$toolSurfaceDecisionPath = Join-Path $RepoRoot $toolSurfaceDecisionRelativePath
$releasePolicyPath = Join-Path $RepoRoot 'docs/release-policy.md'
if (-not [System.IO.File]::Exists($toolSurfaceDecisionPath)) {
    $issues.Add("Missing tool-surface policy decision: $toolSurfaceDecisionRelativePath")
}
else {
    $toolSurfaceDecision = [System.IO.File]::ReadAllText($toolSurfaceDecisionPath)
    $requiredDecisionStatements = @(
        '| Formatting |',
        '| Text edit |',
        '| Code transform |',
        '| File lifecycle |',
        '| Project file |',
        'Prohibit apply-route consolidation across risk buckets',
        'callable for at least one released minor version',
        'Remove it only in the next major version'
    )
    foreach ($requiredStatement in $requiredDecisionStatements) {
        if (-not $toolSurfaceDecision.Contains($requiredStatement, [System.StringComparison]::Ordinal)) {
            $issues.Add("Tool-surface policy is missing required statement: $requiredStatement")
        }
    }
}

if (-not [System.IO.File]::Exists($releasePolicyPath)) {
    $issues.Add('Missing release policy: docs/release-policy.md')
}
else {
    $releasePolicy = [System.IO.File]::ReadAllText($releasePolicyPath)
    $requiredDecisionLink = '(decisions/0009-tool-surface-policy.md)'
    if (-not $releasePolicy.Contains($requiredDecisionLink, [System.StringComparison]::Ordinal)) {
        $issues.Add("Release policy does not link ADR 0009: $requiredDecisionLink")
    }
}

$prReconcilerRelativePath = '.claude/agents/pr-reconciler.md'
$prReconcilerPath = Join-Path $RepoRoot $prReconcilerRelativePath
$backlogAddendaRelativePath = 'ai_docs/prompts/backlog-sweep-addenda.md'
$backlogAddendaPath = Join-Path $RepoRoot $backlogAddendaRelativePath

# pr-reconciler-fail-closed-ship-cleanup: both the reconciler and its repository addenda are
# readiness-only documents. Required handoff words alone are insufficient: a destructive command
# in either body would let an autonomous reader bypass the parent-owned /ship cleanup boundary.
$unsafeCleanupRules = @(
    @{
        Pattern = '(?im)^\s*git\b[^\r\n]*\bworktree\s+remove\b'
        Description = 'worktree removal'
        Examples = @(
            'git worktree remove --force <path>',
            'git -C <repo> worktree remove -f <path>',
            'git -C "C:/repo with spaces" worktree remove --force <path>',
            'git worktree remove <path>'
        )
    },
    @{
        Pattern = '(?im)^\s*gh\b[^\r\n]*\bpr\s+merge\b'
        Description = 'PR merge'
        Examples = @('gh pr merge 123 --merge')
    },
    @{
        Pattern = '(?im)^\s*git\b[^\r\n]*\bbranch\b[^\r\n]*(?:\s-[dD]\b|\s--delete\b)'
        Description = 'local branch deletion'
        Examples = @(
            'git branch -D remediation/example',
            'git -C <repo> branch --delete remediation/example',
            'git -C "C:/repo with spaces" branch --delete remediation/example'
        )
    },
    @{
        Pattern = '(?im)^\s*git\b[^\r\n]*\bpush\b[^\r\n]*(?:\s--delete\b|\s\+?:\S+)'
        Description = 'remote branch deletion'
        Examples = @(
            'git push origin --delete remediation/example',
            'git push origin :remediation/example',
            'git -C "C:/repo with spaces" push origin +:refs/heads/remediation/example'
        )
    },
    @{
        Pattern = '(?is)(?:\b(?:cleanup|worktree|branch)\b.{0,160}\b(?:fails?|failed|failure|errors?)\b.{0,160}\b(?:continue|proceed|ignore|report\s+success|mark\s+success)\b|\b(?:continue|proceed|ignore|report\s+success|mark\s+success)\b.{0,160}\b(?:cleanup|worktree|branch)\b.{0,160}\b(?:fails?|failed|failure|errors?)\b)'
        Description = 'continuation after cleanup failure'
        Examples = @('If worktree removal fails, continue with reconciliation.', 'Proceed after branch cleanup failure.')
    }
)

foreach ($unsafeRule in $unsafeCleanupRules) {
    foreach ($example in $unsafeRule.Examples) {
        if (-not [regex]::IsMatch($example, $unsafeRule.Pattern)) {
            $issues.Add("Readiness-only guard does not reject its required unsafe example: $($unsafeRule.Description) -> $example")
        }
    }
}

# Keep the rejection rules narrowly tied to destructive operations. These representative
# readiness-only instructions must remain valid as the rules evolve.
$safeReadinessExamples = @(
    'gh pr view 123 --json state,mergeable,mergeStateStatus',
    'HANDOFF: parent /ship --land=<pr>',
    'Never force-push, merge, delete a branch, or remove a worktree.',
    'git -C "C:/repo with spaces" worktree list',
    'git branch --show-current',
    'git push origin remediation/example'
)
foreach ($safeExample in $safeReadinessExamples) {
    foreach ($unsafeRule in $unsafeCleanupRules) {
        if ([regex]::IsMatch($safeExample, $unsafeRule.Pattern)) {
            $issues.Add("Readiness-only guard rejects a required safe example: $($unsafeRule.Description) -> $safeExample")
        }
    }
}

if (-not [System.IO.File]::Exists($prReconcilerPath)) {
    $issues.Add("Missing PR reconciler guidance: $prReconcilerRelativePath")
}
else {
    $prReconciler = [System.IO.File]::ReadAllText($prReconcilerPath)
    $requiredReconcilerStatements = @(
        'readiness-only',
        'HANDOFF: parent /ship --land=<pr>',
        'SHIP_STATUS=landed-cleanup-failed',
        'Evaluate `state` before all other fields',
        'If `gh pr view` fails, wait 5 seconds and retry once.',
        'Never force-push, merge, delete a branch, or remove a worktree.'
    )
    foreach ($requiredStatement in $requiredReconcilerStatements) {
        if (-not $prReconciler.Contains($requiredStatement, [System.StringComparison]::Ordinal)) {
            $issues.Add("PR reconciler guidance is missing required fail-closed statement: $requiredStatement")
        }
    }

    foreach ($unsafeRule in $unsafeCleanupRules) {
        if ([regex]::IsMatch($prReconciler, $unsafeRule.Pattern)) {
            $issues.Add("PR reconciler guidance contains unsafe cleanup instruction: $($unsafeRule.Description)")
        }
    }
}

if (-not [System.IO.File]::Exists($backlogAddendaPath)) {
    $issues.Add("Missing backlog remediation addenda: $backlogAddendaRelativePath")
}
else {
    $backlogAddenda = [System.IO.File]::ReadAllText($backlogAddendaPath)
    $requiredAddendaStatements = @(
        'readiness-only; parent owns /ship --land=<pr>',
        'SHIP_STATUS=landed-cleanup-failed'
    )
    foreach ($requiredStatement in $requiredAddendaStatements) {
        if (-not $backlogAddenda.Contains($requiredStatement, [System.StringComparison]::Ordinal)) {
            $issues.Add("Backlog remediation addenda is missing required reconciler handoff statement: $requiredStatement")
        }
    }

    foreach ($unsafeRule in $unsafeCleanupRules) {
        if ([regex]::IsMatch($backlogAddenda, $unsafeRule.Pattern)) {
            $issues.Add("Backlog remediation addenda contains unsafe cleanup instruction: $($unsafeRule.Description)")
        }
    }
}

# backlog-intake-v15-writer-contract: raw audit severities are source metadata, while
# `ai_docs/backlog.md` has v15 named bands. The intake prompt used to mix both vocabularies
# and prescribe direct table writes, which bypasses the transactional row/detail writer.
$backlogIntakeContractPaths = @(
    '.claude/skills/backlog-intake/SKILL.md',
    '.claude/agents/backlog-intake-extractor.md',
    'ai_docs/items/backlog-d-fragment-schema.md'
)
$backlogIntakeContents = @{}
foreach ($relativePath in $backlogIntakeContractPaths) {
    $path = Join-Path $RepoRoot $relativePath
    if (-not [System.IO.File]::Exists($path)) {
        $issues.Add("Missing backlog-intake v15 contract: $relativePath")
        continue
    }

    $backlogIntakeContents[$relativePath] = [System.IO.File]::ReadAllText($path)
}

$rawSeverityMapPattern = '(?s)P0\s*-\>\s*Critical.*P1\s*-\>\s*High.*P2\s*-\>\s*Medium.*P3\s*-\>\s*Low'
foreach ($relativePath in $backlogIntakeContractPaths) {
    if ($backlogIntakeContents.ContainsKey($relativePath) -and -not [regex]::IsMatch($backlogIntakeContents[$relativePath], $rawSeverityMapPattern)) {
        $issues.Add("Backlog-intake contract is missing the raw P0/P1/P2/P3 to v15-band mapping: $relativePath")
    }
}

$requiredBacklogIntakeStatements = @{
    '.claude/skills/backlog-intake/SKILL.md' = @(
        'Every mutation of `ai_docs/backlog.md`, its paired `ai_docs/items/<id>.md` files, its preamble, or its `updated_at` stamp uses the global writer',
        'Do not hand-edit, append, re-sort, or count the backlog table.',
        'backlog.mjs count <repo-root>'
    )
    '.claude/agents/backlog-intake-extractor.md' = @(
        'The caller persists accepted rows, refinements, detail notes, preamble changes, and timestamps through the global `node ~/.claude/scripts/backlog.mjs` writer.',
        'Do NOT prescribe a direct table edit.'
    )
    'ai_docs/items/backlog-d-fragment-schema.md' = @(
        'Every mutation of the local backlog row/detail pair, preamble, or `updated_at` stamp uses the global `node ~/.claude/scripts/backlog.mjs` writer.',
        'Do not hand-edit or append the `ai_docs/backlog.md` table.'
    )
}
foreach ($relativePath in $requiredBacklogIntakeStatements.Keys) {
    if (-not $backlogIntakeContents.ContainsKey($relativePath)) {
        continue
    }

    foreach ($requiredStatement in $requiredBacklogIntakeStatements[$relativePath]) {
        if (-not $backlogIntakeContents[$relativePath].Contains($requiredStatement, [System.StringComparison]::Ordinal)) {
            $issues.Add("Backlog-intake contract is missing required writer guidance: $relativePath -> $requiredStatement")
        }
    }
}

$activeIntakeGuidancePaths = @(
    '.claude/skills/backlog-intake/SKILL.md',
    '.claude/agents/backlog-intake-extractor.md'
)
$retiredIntakePriorityRules = @(
    @{
        Pattern = '(?im)^\s*##\s*P2\s*/\s*P3\s*/\s*P4\b'
        Description = 'retired P2/P3/P4 priority heading'
        Examples = @('## P2 / P3 / P4 — open work')
    },
    @{
        Pattern = '(?im)^\s*-\s+\*\*P[0-4]\*\*:'
        Description = 'retired raw-P local-priority classifier'
        Examples = @('- **P3**: meaningful friction / gap.')
    },
    @{
        Pattern = '(?i)\bP2\s*\|\s*P3\s*\|\s*P4\b'
        Description = 'retired P2|P3|P4 output enum'
        Examples = @('| `<kebab-id>` | P2|P3|P4 | — | summary |')
    },
    @{
        Pattern = '(?i)\bP0\s*\|\s*P1\s*\|\s*P2\s*\|\s*P3\b'
        Description = 'raw P0|P1|P2|P3 local-priority output enum'
        Examples = @('| `<kebab-id>` | P0|P1|P2|P3 | — | summary |')
    },
    @{
        Pattern = '(?im)\bbacklog\.mjs\b[^\r\n]*\s--pri\s+P[0-4]\b'
        Description = 'retired P severity passed as a local writer priority'
        Examples = @(
            'node ~/.claude/scripts/backlog.mjs add . --id raw-priority --pri P0 --do-file row-do.md --items-file row-detail.md',
            'node ~/.claude/scripts/backlog.mjs add . --id retired-priority --pri P4 --do-file row-do.md --items-file row-detail.md'
        )
    },
    @{
        Pattern = '(?im)^\s*\|[^|\r\n]*\|\s*P[0-4]\s*\|'
        Description = 'retired P severity in a local priority-table cell'
        Examples = @('| `retired-priority` | P4 | — | summary |')
    },
    @{
        Pattern = '(?im)\b(?:local\s+)?(?:pri|priority)\s*(?:may\s+be|is|=|:)\s*P[0-4]\b'
        Description = 'retired P severity described as a local priority'
        Examples = @('The local priority is P4 for a speculative improvement.')
    },
    @{
        Pattern = '(?im)\b(?:Backlog now|Final)\s*:\s*\{P2(?:-count)?\}\s*P2\s*\+\s*\{P3(?:-count)?\}\s*P3\s*\+\s*\{P4(?:-count)?\}\s*P4\b'
        Description = 'stale manually maintained P2/P3/P4 count'
        Examples = @('Final: {P2} P2 + {P3} P3 + {P4} P4 = {total} open rows')
    }
)
$directIntakeMutationRules = @(
    @{
        Pattern = '(?im)\b(?:append|add)(?:s|ed|ing)?\s+(?:(?:a|the)\s+)?(?:new\s+)?rows?\s+(?:to|in)\s+(?:the\s+)?(?:matching\s+)?priority\s+band\b'
        Description = 'direct priority-band append'
        Examples = @(
            'Append the row to the matching priority band.',
            'Add a new row to the matching priority band.',
            'Do not hesitate to append a new row to the matching priority band.'
        )
    },
    @{
        Pattern = '(?im)\bUse\s+(?:Edit|MultiEdit|Write)\b.{0,200}\b(?:backlog\.md|row(?:s)?|do\s+cell)\b'
        Description = 'direct editor backlog mutation'
        Examples = @(
            'Use Edit with exact-string replace on the affected backlog row.',
            'Use MultiEdit to rewrite the backlog.md table.',
            'Use Write to replace the do cell.',
            'Never wait for the writer; use Edit to change ai_docs/backlog.md.'
        )
    },
    @{
        Pattern = '(?im)\bdirectly\s+(?:append|add|edit|rewrite|update|sort)\b.{0,160}\b(?:backlog(?:\.md)?|table|row(?:s)?|do\s+cell)\b'
        Description = 'direct table mutation'
        Examples = @('Directly edit the backlog table after filtering.')
    },
    @{
        Pattern = '(?im)^\s*(?:Set-Content|Add-Content|Out-File|sed\s+-i|perl\s+-pi)\b[^\r\n]*\bai_docs/backlog\.md\b'
        Description = 'direct file-write command for backlog.md'
        Examples = @(
            'Set-Content ai_docs/backlog.md $rewritten',
            'Add-Content ai_docs/backlog.md $row',
            'Out-File -FilePath ai_docs/backlog.md -InputObject $row',
            'sed -i "s/old/new/" ai_docs/backlog.md',
            'perl -pi -e "s/old/new/" ai_docs/backlog.md'
        )
    },
    @{
        Pattern = '(?im)^\s*(?:echo|printf)\b[^\r\n]*(?:>>|>)\s*["'']?ai_docs/backlog\.md\b'
        Description = 'shell redirection write for backlog.md'
        Examples = @('echo "row" >> ai_docs/backlog.md')
    },
    @{
        Pattern = '(?im)^\s*(?:git\b[^\r\n]*\b(?:mv|rm)\b|(?:Remove|Move)-Item)\b[^\r\n]*\bai_docs/backlog\.md\b'
        Description = 'direct backlog-table move or deletion'
        Examples = @(
            'git rm ai_docs/backlog.md',
            'git mv ai_docs/backlog.md ai_docs/archive/backlog.md',
            'git -C C:/repo rm ai_docs/backlog.md',
            'Remove-Item ai_docs/backlog.md',
            'Move-Item ai_docs/backlog.md ai_docs/archive/backlog.md'
        )
    }
)
$backlogIntakeGuardRules = @($retiredIntakePriorityRules) + @($directIntakeMutationRules)

function Test-UnsafeBacklogIntakeGuidance {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Pattern
    )

    foreach ($match in [regex]::Matches($Text, $Pattern)) {
        $lineStart = $Text.LastIndexOf("`n", [Math]::Max(0, $match.Index - 1))
        if ($lineStart -lt 0) {
            $lineStart = 0
        }
        else {
            $lineStart++
        }

        $lineEnd = $Text.IndexOf("`n", $match.Index)
        if ($lineEnd -lt 0) {
            $lineEnd = $Text.Length
        }

        $line = $Text.Substring($lineStart, $lineEnd - $lineStart).TrimEnd("`r")
        $beforeMatch = $line.Substring(0, $match.Index - $lineStart)
        if ($beforeMatch -match '(?i)\b(?:Do\s+not|Don''t|Never)\s+(?:(?:run|use|directly)\s+)?$') {
            continue
        }

        return $true
    }

    return $false
}

foreach ($retiredRule in $backlogIntakeGuardRules) {
    foreach ($example in $retiredRule.Examples) {
        if (-not (Test-UnsafeBacklogIntakeGuidance -Text $example -Pattern $retiredRule.Pattern)) {
            $issues.Add("Backlog-intake guard does not reject its required unsafe example: $($retiredRule.Description) -> $example")
        }
    }
}

# Raw external severity and artifact archival remain valid concepts. These examples ensure the
# retired-guidance rules stay narrow rather than rejecting the v15 mapping or safe bookkeeping.
$safeBacklogIntakeExamples = @(
    'Raw external-source severity maps only as P0 -> Critical, P1 -> High, P2 -> Medium, P3 -> Low.',
    'node ~/.claude/scripts/backlog.mjs add . --id example-row --pri Medium --do-file row-do.md --items-file row-detail.md',
    'node ~/.claude/scripts/backlog.mjs count .',
    'git mv review-inbox/example.md review-inbox/archive/20260910T000000Z/example.md',
    'git rm C:/Code-Repo/Sibling/backlog.d/example-row.md',
    'Do not append a new row to the matching priority band; use backlog.mjs add instead.',
    'Do not use Edit to change ai_docs/backlog.md; use backlog.mjs update instead.',
    'Never directly rewrite the backlog table; use backlog.mjs update instead.',
    'echo "fragment" >> C:/Code-Repo/Sibling/backlog.d/example-row.md',
    'Move-Item review-inbox/example.md review-inbox/archive/example.md',
    'severity: P2 is raw source metadata, not a local priority.',
    'Do not edit the table directly; use backlog.mjs update --do-file instead.'
)
foreach ($safeExample in $safeBacklogIntakeExamples) {
    foreach ($retiredRule in $backlogIntakeGuardRules) {
        if (Test-UnsafeBacklogIntakeGuidance -Text $safeExample -Pattern $retiredRule.Pattern) {
            $issues.Add("Backlog-intake guard rejects a required safe example: $($retiredRule.Description) -> $safeExample")
        }
    }
}

foreach ($relativePath in $activeIntakeGuidancePaths) {
    if (-not $backlogIntakeContents.ContainsKey($relativePath)) {
        continue
    }

    foreach ($retiredRule in $retiredIntakePriorityRules) {
        if (Test-UnsafeBacklogIntakeGuidance -Text $backlogIntakeContents[$relativePath] -Pattern $retiredRule.Pattern) {
            $issues.Add("Backlog-intake guidance contains $($retiredRule.Description): $relativePath")
        }
    }
}

# The schema legitimately documents raw P0-P3 source fields, so apply the retired-priority
# checks only to documents that emit local priorities. Direct-mutation checks, however, protect
# every intake contract document, including the schema that tells operators when deletion is safe.
foreach ($relativePath in $backlogIntakeContractPaths) {
    if (-not $backlogIntakeContents.ContainsKey($relativePath)) {
        continue
    }

    foreach ($directRule in $directIntakeMutationRules) {
        if (Test-UnsafeBacklogIntakeGuidance -Text $backlogIntakeContents[$relativePath] -Pattern $directRule.Pattern) {
            $issues.Add("Backlog-intake contract contains $($directRule.Description): $relativePath")
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "AI docs validation passed."

# Shipped-skill generality check runs as part of doc-side validation (not
# release-side), so the check still gates a PR even when CI's detect-docs-only
# step skips verify-release.ps1. Both `./skills/**/SKILL.md` markdown bodies and
# any future `./skills/**/*.md` additions are validated by verify-skills-are-
# generic.ps1. verify-release.ps1 invokes this script too, so local full-release
# runs continue to catch the same violations.
& (Join-Path $PSScriptRoot 'verify-skills-are-generic.ps1') -RepoRoot $RepoRoot
