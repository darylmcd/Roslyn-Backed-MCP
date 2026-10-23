# workspace-native-load-cleanup-gate-release — Release native load ownership after disposal failures

**row:** `workspace-native-load-cleanup-gate-release` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Helpers/OwnedResourceCleanup.cs`
- `tests/RoslynMcp.Tests/WorkspaceNativeCleanupFailureTests.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceCloseCleanupException.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`

## Acceptance

- [ ] Attempt workspace and analyzer-lease cleanup independently on both publication and failure branches; clear workspace references before lease release.
- [ ] Always release the session load semaphore, including disposal exceptions.
- [ ] Preserve an existing typed primary failure/cancellation with safe bounded cleanup outcomes; aggregate failures only when no primary exists.
- [ ] Prove actual owned-native lease failures on old and corrected behavior and verify a subsequent native reload can acquire the same gate.

- [ ] Attempt all session, close and shutdown cleanup independently; preserve capacity, notifications and load-gate disposal after a native resource fault.
- [ ] Carry exact completed-removal ownership through host close, eviction and gated or ungated retirement; remove execution gates only after writer release.
- [ ] Attempt requested post-close process draining even when native or execution-gate cleanup fails; preserve all failures.

## Evidence

- Source review 2026-10-16: sequential workspace/lease Dispose calls preceded LoadLock.Release in one finally; either disposal could skip companion cleanup and permanently retain the load gate.
- Complete shared cleanup/gate-finally source correction is prepared. Parent owns real-native lease fault injection, guarded old behavior and fresh runtime. Acceptance remains open.

| 2026-10-16 bounded native evidence | Result | Limit |
|---|---|---|
| Fresh first focused snapshot | Both real Windows analyzer-handle cleanup rows passed within focused82; fixed cleanup/manager11 also passed. | Windows analyzer isolation has no corresponding Linux fault route; no Linux native-fault claim. |
| Localized old native-load finally | Both rows failed at actual semaphore state expected1/actual0 after the controlled native-handle fault;25s. Exact Manager source restoration verified in manager-lifecycle-old-NativeCleanup-e91317e8449d4ae2be132e3d50296ddd/receipt.json (Before=After5EF93D...62B8). | Other required Session.Dispose, CloseCore and manager shutdown cleanup copies are still old pending additional causal controls. |

| 2026-10-16 lifecycle breadth qualification | Result | Limit |
|---|---|---|
| Actual native Close/Shutdown OLD controls | Both failed after actual analyzer-lease release failure: Close had removed the session but retained its capacity; shutdown retained two sessions after the first cleanup fault. | Distinct from the earlier native reload gate controls. |
| Actual host close and gated retirement OLD controls | Both failed exact execution-gate removal assertions after actual native fault, completed session removal and writer/task completion (host-retirement-old-core-first1). | Corrected collector/receipt source awaited fresh runtime. |
| Additional lifecycle omissions OLD2 | Both valid failures in lifecycle-omissions-old1/lifecycle-omissions-old.trx, 15s: successful native close plus gate failure skipped requested process drain (expected1/actual0); supported ungated retirement faulted its owned task with the completed-removal receipt instead of reporting it. | All native fault/session-removal and task-completion premises passed. |
| Current source | Shared collectors now attempt session/close/shutdown cleanup; an owner-bound completed-removal aggregate preserves gate cleanup after writer release. Host carries native/gate failures through process drain; ungated retirement reports its receipt once. | Source-only latest omissions correction; fresh fixed gate still required. Row remains OPEN. |


| Fresh corrected lifecycle qualification | Result | Limit |
|---|---|---|
| acquisition-file-old-lifecycle-fixed1/acquisition-file-old-lifecycle-fixed.trx | All 9 WorkspaceNativeCleanupFailureTests rows passed: reload publication/failure2, Close1, shutdown1, host native-fault drain false/true2, gated retirement1, successful-close gate-fault drain1, ungated retirement1. TRX definitions/results independently read in this session. | Windows actual owned analyzer lifetime route; bounded class result, not a full gate. Whole capture15 had12 passes and3 expected old physical cleanup failures. Row remains OPEN pending initiative shipping/verification. |
