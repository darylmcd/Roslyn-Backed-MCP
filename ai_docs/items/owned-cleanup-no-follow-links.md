# owned-cleanup-no-follow-links — owned-cleanup-no-follow-links

**row:** `owned-cleanup-no-follow-links` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalGraphStage.cs`
- `tests/RoslynMcp.Tests/RestoreBoundaryRegressionTests.cs`

## Acceptance

- [ ] Remove root and nested links themselves while external target bytes and read-only attributes remain unchanged.
- [ ] Cover Windows junctions, supported native symlinks, dangling links, ordinary owned read-only files, cancellation and cleanup failure reporting.

## Evidence

- Independent current-source restore review on 2026-10-16 identified this mechanism. Parent serializes causal regression tests before correction.

## Context

- Required complete-fix companion of the operator-authorized restore initiative; do not defer defects exposed by the active change.
