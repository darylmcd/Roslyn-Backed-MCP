# workspace-cache-retention-policy — Decide bounded persisted cache retention

**row:** `workspace-cache-retention-policy` · **pri:** `Low` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheStore.cs:34`

## Acceptance

- [ ] Measure entry growth across graph/runtime/solution changes and choose an explicit owned retention policy.
- [ ] Preserve active readers/writers, corrupt-entry behavior, cancellation and no-follow ownership; do not infer deletion authority from age alone.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Retained independent item records unbounded entry growth; current store is still present and no matching retention owner was found. Fresh measurement is required before policy or implementation.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
