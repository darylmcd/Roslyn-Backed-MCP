# dangling-plan-path-comments-tools-c — Drop dangling plan-path comments (Tools C)

**row:** `dangling-plan-path-comments-tools-c` · **pri:** `Low` · **size:** `M`

# dangling-plan-path-comments-tools-c — Drop dangling plan-path comments (Tools C)

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TypeExtractionTools.cs`

## Acceptance

- [ ] No comment in the listed files cites a deleted `ai_docs/plans/` path.
- [ ] Comment-only change; build unchanged.

## Evidence

- Path scan found the files citing plan files removed by fa2b2b12 and by sweep-plan GC. (doc-audit 2026-09-23)
