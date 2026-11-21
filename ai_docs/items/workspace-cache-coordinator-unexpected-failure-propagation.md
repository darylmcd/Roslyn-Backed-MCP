# workspace-cache-coordinator-unexpected-failure-propagation — Do not hide cache-coordinator programming failures

**row:** `workspace-cache-coordinator-unexpected-failure-propagation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:41`
- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:79`
- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:110`

## Acceptance

- [ ] Inventory all three broad best-effort fallback boundaries and handle only expected cache/evaluation/read/parse failures.
- [ ] Propagate unexpected programming failures with original exception identity; preserve intentional cache-unavailable cold-load fallback and diagnostics.
- [ ] Observe actual coordinator old-red/new-green propagation with deterministic failing dependencies plus ordinary/expected failure controls. Do not weaken cache identity, ownership, cancellation or native load checks.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Independent cold review identified TryProbeAsync, ResolveAndWriteAsync and WriteFreshEntryAsync catching every non-cancellation exception and returning null. Current source retains all three boundaries. This is distinct from graph framing, retention and proposal-only path authorization.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.

## Notes

5.x screen at d9e2b3ff. TryProbeAsync, ResolveAndWriteAsync and WriteFreshEntryAsync all catch unexpected non-cancellation errors. WorkspaceManager calls the pre-open probe and both post-load writers as part of stable workspace_load/reload. A complete propagation fix can turn a successful cold load into a tool failure. Select after qualifying the stable failure and recovery contract, or in the next approved major.

Rechecked 5.1.1 base 9e7a9439. TryProbeAsync and both cache-write paths still return null on unexpected non-cancellation exceptions. WorkspaceManager.cs:1516 and :1579–1585 call them before publishing the stable workspace snapshot. Require raw evidence for initial load and reload under both supported protocol eras before concluding propagation fits 5.x. If the complete fix changes supported cold-load success/recovery into an error, reopen at the next operator-approved major with ADR, migration note and dual-era regression coverage.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
