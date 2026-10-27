# workspace-cache-coordinator-unexpected-failure-propagation — Do not hide cache-coordinator programming failures

**row:** `workspace-cache-coordinator-unexpected-failure-propagation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:37`
- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:74`
- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:106`

## Acceptance

- [ ] Inventory all three broad best-effort fallback boundaries and handle only expected cache/evaluation/read/parse failures.
- [ ] Propagate unexpected programming failures with original exception identity; preserve intentional cache-unavailable cold-load fallback and diagnostics.
- [ ] Observe actual coordinator old-red/new-green propagation with deterministic failing dependencies plus ordinary/expected failure controls. Do not weaken cache identity, ownership, cancellation or native load checks.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Independent cold review identified TryProbeAsync, ResolveAndWriteAsync and WriteFreshEntryAsync catching every non-cancellation exception and returning null. Current source retains all three boundaries. This is distinct from graph framing, retention and proposal-only path authorization.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
