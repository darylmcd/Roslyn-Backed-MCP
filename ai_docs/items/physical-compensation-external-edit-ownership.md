# physical-compensation-external-edit-ownership — physical-compensation-external-edit-ownership

**row:** `physical-compensation-external-edit-ownership` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalMutationFileGuard.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`

## Acceptance

- [ ] An external edit between operation writes or before failed-operation compensation remains byte-identical and retains a recovery barrier.
- [ ] Reauthorize before every guard read/write; only exact entry or intended post-state permits compensation; retain cancellation and successful retry controls.

## Evidence

- Independent current-source restore review on 2026-10-16 identified this mechanism. Parent serializes causal regression tests before correction.

## Context

- Required complete-fix companion of the operator-authorized restore initiative; do not defer defects exposed by the active change.

2026-10-16 retained-recovery causal extension: RetainedRecovery_ReadAuthorityRejectsUnknownBytesAfterInitialOwnershipCheck unknown row deletes independently injected external bytes on old production; known entry and registered-intent controls pass. Require pure AssertKnownFileRead(operation, observed) over the exact live-authorized read buffer before accepting entry/prewrite/rollback authority; never certify an independently reread buffer. Required manager authority and strict delegates must enforce this for retained recovery and owned compensation. Parent combined old gate: 8 failed / 4 passed (including independent output roots and duplicate ownership), 1m27; production verification pending.

## Current authorization callback and destination ownership extension

| Field | Evidence |
|---|---|
| Existing row | `physical-compensation-external-edit-ownership`; update this row, do not create a duplicate. |
| Exposed mechanism | Exact entry/intended-post read is followed by an independently awaited write authorization and a blind destructive destination operation. The callback can introduce unknown bytes after the guard. |
| Production anchors | `src/RoslynMcp.Roslyn/Services/UndoService.cs:458`, `:488`, `:494`; `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:107`, `:157`; `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:89`, `:198`; `src/RoslynMcp.Roslyn/Services/PhysicalMutationFileGuard.cs:16`. |
| New regression | `tests/RoslynMcp.Tests/UndoWriteAuthorizationOwnershipTests.cs`: primary inverse deletion and replacement, package-free native single-project graph, real recovery authority with forwarding lease decorator. |
| Injection boundary | A real inverse `BeforeWrite` arms one subsequent target authorization callback; callback forwards current authority then installs external bytes. No callback ordinals or deadline changes. |
| Runtime status | Pending parent execution. Scratch test patch passes git apply --check only; source reasoning is not causal runtime proof. |

### Additional acceptance

- [ ] External bytes introduced by current write authorization survive primary inverse deletion and replacement; refuse successful publication and retain the original undo target plus recovery barrier.
- [ ] After explicit conflict resolution, retained recovery succeeds and the original inverse remains retryable; exact pre-operation bytes or missing state are restored on that retry.
- [ ] Apply the same expected-destination transition contract to document persistence, composite writes and compensation; never certify an independently reread buffer.
- [ ] Bind the destructive operation to the validated destination object or retain lossless displaced-entry custody. Do not replace the gap with another pathname recheck before an unconditional unlink/overwrite.
- [ ] Preserve existing metadata, encoding, current grants, cleanup failures, cancellation and platform-specific physical identity behavior. Qualify native handoff semantics separately from deterministic callback controls.
- [ ] Keep existing same-operation external-edit and successful retry assertions unchanged; execute new controls on old behavior before accepting a fix.

The source-transition custody design remains a proposal, not approved production scope or evidence of an implemented guarantee.

## 2026-10-17: Namespace-permission migration qualification

| Field | Evidence / requirement |
|---|---|
| Root mechanism | Expected-source transitions require namespace permissions absent from the previous direct data overwrite. Do not weaken the external-edit boundary with an unbound overwrite fallback. |
| Accepted migration | Next-major ADR 0029 requires native source rename/delete permission plus existing staging access; path-policy grants remain independent. Package restore cannot repair OS permission refusal. |
| Windows causal premise | Existing data overwrite succeeds with current-user file Delete and parent DeleteSubdirectoriesAndFiles denied. The transition refuses with exact IOException, preserves source bytes, and creates no custody receipt. Fresh Access security descriptors restore the exact original DACLs. |
| POSIX causal premise | Existing data overwrite succeeds with source parent lacking write permission. The transition refuses with exact IOException; source bytes and namespace remain unchanged; original permissions are restored. |
| Windows runtime | permission132.log / permission132/results.trx: 1 passed, 1 platform skip, 0 failed. Parent executed and inspected this session. |
| Linux runtime | namespace129-linux/run.log / namespace129-linux/results.trx: 38 passed, 7 platform skips, 0 failed; includes the Unix permission control and native transition matrix. Parent executed this session. |
| Evidence root | %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume |
| Naming-only finalization | Qualified diagnostic test renamed SourceTransitionPermissionTests; test methods renamed RefusesWithoutChangingSource. Assertions and cleanup unchanged after qualified run; final named source awaits parent gate. |
| Scope | Add to existing physical-compensation-external-edit-ownership row. Do not create a duplicate mechanism row or mark full prerequisite verification complete. |

### Anchors

- src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryLease.cs:159 — Windows required source handle permissions.
- src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryScope.cs:628 — POSIX no-replace namespace transfer.
- tests/RoslynMcp.Tests/SourceTransitionPermissionTests.cs:14 — Windows data-write versus namespace permission control.
- tests/RoslynMcp.Tests/SourceTransitionPermissionTests.cs:73 — POSIX parent permission control.
- docs/decisions/0029-explicit-preview-snapshot-authority.md:35 — accepted permission contract and migration.
- changelog.d/explicit-preview-snapshot-authority.md:4 — next-major consumer migration.

2026-10-17 targeted repair: WrongPersistedMetadata original fixture bypassed native CommitRestoration by injecting only CommitMutation. Shared FailClosedWorkspaceManagerStub.ForRecovery now centralizes real authority forwarding; metadata injection is explicitly native and asserts reachability/changed bytes. Existing earlier physical-input guard rejects before semantic reference comparison; exact earlier-refusal oracle proposal awaits operator approval, so this case remains red. Simplified interop fixture contains SampleApp/SampleLib/Interop without unrelated sample test packages; ReferenceMetadataReplacement_IsReviewedAndPersistsWithoutLosingTheEdge passes in targeted interop-roundtrip1 (3m21). All original method names/TestMethod/DataRow attributes preserved. Three PreparedUndoCaller stale injection users remain identified; do not claim complete closure.

2026-10-17 superseding targeted evidence: operator approved the exact earlier physical-input refusal oracle. WrongPersistedMetadata now passes every recovery/bytes/version/preview/undo/subsequent-apply assertion (metadata-approved, 3m53.342s). All three PreparedUndoCaller rejection cases reproduced failure on old native injection wiring, then passed after shared ForRecovery native refusal callbacks plus mandatory reachability assertions (prepared-undo-shared-recovery corrected TRX:16PASS including13 native serialization controls). Existing assertions remain; no production authority or deadline changes. Full qualification/shipping remains pending.
