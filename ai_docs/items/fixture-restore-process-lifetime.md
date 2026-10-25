# fixture-restore-process-lifetime — Bound fixture-owned restore processes

**row:** `fixture-restore-process-lifetime` · **pri:** `Medium` · **size:** `M` · **deps:** —

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs:31`
- `src/RoslynMcp.Roslyn/Services/DotnetCommandRunner.cs`

## Acceptance

- [ ] Inventory current shared/direct fixture restore callers using no cancellation; decompose coherent fixture batches if needed.
- [ ] Provide fixture-owned bounded setup lifetime with caller cancellation, tree termination/output drain and primary/cleanup preservation; derive bounds from measured normal behavior.
- [ ] Observe a deterministic non-exiting owned child fail old lifetime proof; retain real restore positives and production deadlines.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current shared fixture restore forwards optional CancellationToken defaulting to None. Retained branch also contains proposal-only direct native fixtures; do not list absent tests as current edit targets.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
