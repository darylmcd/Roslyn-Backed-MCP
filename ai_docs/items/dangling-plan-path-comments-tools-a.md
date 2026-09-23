# dangling-plan-path-comments-tools-a — Drop dangling plan-path comments (Tools A)

**row:** `dangling-plan-path-comments-tools-a` · **pri:** `Low` · **size:** `M`

# dangling-plan-path-comments-tools-a — Drop dangling plan-path comments (Tools A)

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/BulkRefactoringTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/DeadCodeTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ExtractMethodTools.cs`

## Acceptance

- [ ] No comment in the listed files cites a deleted `ai_docs/plans/` path.
- [ ] Comment-only change; build unchanged.

## Evidence

- Path scan found the files citing plan files removed by fa2b2b12 and by sweep-plan GC. (doc-audit 2026-09-23)
