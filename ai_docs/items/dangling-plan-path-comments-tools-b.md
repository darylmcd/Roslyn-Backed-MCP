# dangling-plan-path-comments-tools-b — Drop dangling plan-path comments (Tools B)

**row:** `dangling-plan-path-comments-tools-b` · **pri:** `Low` · **size:** `M`

# dangling-plan-path-comments-tools-b — Drop dangling plan-path comments (Tools B)

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/InterfaceExtractionTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/MultiFileEditTools.cs`

## Acceptance

- [ ] No comment in the listed files cites a deleted `ai_docs/plans/` path.
- [ ] Comment-only change; build unchanged.

## Evidence

- Path scan found the files citing plan files removed by fa2b2b12 and by sweep-plan GC. (doc-audit 2026-09-23)
