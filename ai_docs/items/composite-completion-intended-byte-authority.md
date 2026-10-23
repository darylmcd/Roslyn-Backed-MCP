# composite-completion-intended-byte-authority — composite-completion-intended-byte-authority

**row:** `composite-completion-intended-byte-authority` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.Recovery.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`

## Acceptance

- [ ] External replacement during native commit cannot be published as an owned undo target; preserve bytes and retain recovery.
- [ ] Completed snapshots match last registered write intentions and untouched captured entries, with unique exact path ownership; check each next write and final intended state.

## Evidence

- Independent current-source restore review on 2026-10-16 identified this mechanism. Parent serializes causal regression tests before correction.

## Context

- Required complete-fix companion of the operator-authorized restore initiative; do not defer defects exposed by the active change.
