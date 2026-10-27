# fixture-restore-process-lifetime — Bound fixture-owned restore processes

**row:** `fixture-restore-process-lifetime` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs:31`
- `src/RoslynMcp.Roslyn/Services/DotnetCommandRunner.cs`

## Acceptance

- [ ] Inventory current shared/direct fixture restore callers using no cancellation; decompose coherent fixture batches if needed.
- [ ] Provide fixture-owned bounded setup lifetime with caller cancellation, tree termination/output drain and primary/cleanup preservation; derive bounds from measured normal behavior.
- [ ] Observe a deterministic non-exiting owned child fail old lifetime proof; retain real restore positives and production deadlines.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current shared fixture restore forwards optional CancellationToken defaulting to None. Retained branch also contains proposal-only direct native fixtures; do not list absent tests as current edit targets.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
