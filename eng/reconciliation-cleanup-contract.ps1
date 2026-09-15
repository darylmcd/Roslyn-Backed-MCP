# Normal remediation worktrees belong to canonical shipping. Audit disposal is a separate
# lifecycle contract and must not turn into a blanket removal command in this guidance.
function Get-ReconciliationCleanupIssue {
    param([string]$Content)

    $requiredStatements = @(
        '/ship --land=<pr>',
        'Refuse dirty worktrees',
        'modified, untracked, and ignored residue',
        'SHIP_STATUS=landed-cleanup-failed',
        'Disposable audit exception',
        'never applies to normal initiative worktrees'
    )
    foreach ($statement in $requiredStatements) {
        if (-not $Content.Contains($statement, [System.StringComparison]::Ordinal)) {
            Write-Output "Reconciliation cleanup guidance is missing required statement: $statement"
        }
    }

    # Scan raw Markdown, including inline examples and fenced commands. Unlike link checking,
    # executable guidance inside a code fence is part of this safety contract.
    if ($Content -match '(?im)\bgit\b[^\r\n]*?\bworktree\s+remove\b') {
        Write-Output 'Reconciliation cleanup guidance contains a worktree removal command; hand off to canonical /ship.'
    }
}
