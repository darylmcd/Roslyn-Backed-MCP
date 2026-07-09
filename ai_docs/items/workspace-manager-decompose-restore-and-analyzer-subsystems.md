# workspace-manager-decompose-restore-and-analyzer-subsystems — Split WorkspaceManager god class

**row:** `workspace-manager-decompose-restore-and-analyzer-subsystems` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:22-1775`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:958-1470`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1476-1573`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:192`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:793`

## Acceptance

- [ ] Restore-staleness polling/parsing logic moved out of WorkspaceManager.cs into a new collaborator type with its own unit tests.
- [ ] Analyzer-reference isolation (StripUnresolvedAnalyzerReferencesAsync) moved out into a second collaborator; WorkspaceManager.cs line count and its LoadAsync/LoadIntoSessionAsync cyclomatic complexity both drop measurably.
- [ ] All existing WorkspaceManager-dependent tests still pass unchanged.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03d-roslyn-workspace-infra::DG2-cleanliness, S03d-roslyn-workspace-infra::DG7-config-deps-ergo, S03d-roslyn-workspace-infra::DG1-design
