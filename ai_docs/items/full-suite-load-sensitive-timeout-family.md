# full-suite-load-sensitive-timeout-family — Make the load-sensitive fork, native-build and deadline tests deterministic

**row:** `full-suite-load-sensitive-timeout-family` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/ForkStorageLockOrderTests.cs`:24 (fork-storage family; other classes listed in Evidence)
- `tests/RoslynMcp.Tests/MissingWorkspaceRootRetirementTests.cs`:27
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`:477
- `ai_docs/known-flakes.md`

## Acceptance

- [ ] For each test named in Evidence: the failure under a loaded machine is reproduced (e.g. run alongside a second full suite) and the deadline is replaced by a readiness/event wait or justified with a measured bound.
- [ ] A filter-less run with a concurrent load generator is green; known-flakes.md gets entries only per its discipline (dedicated PR, base proof, row id).

## Evidence

- undo-b (PR 2141): full run Failed 12/6741 (TimeoutException after 1-3 min): `ActualReload_CanFinishBeforeCopyOrDuringForkLoad`, `RetainedFork_ProjectFilePreviewReplay_StripsOnlyPreviousOwnedImport`, `ForkStorageAuthorization_PinsCanonicalPathsAndReauthorizesCollision`, `WorkspaceForkApply_TestsDoNotBlockSourceWrite`, `Sweep_ActualPoolWaiter_PreservesRetainedSource`, `UnchangedPreview_NativePackagePreprocessing_PreservesSource`, `CanonicalPool_InsideOrdinarySource_PreservesAuthoredAssetsAcrossGenerations`; all 22 cases pass alone.
- iface-extraction-b (PR 2148): Failed 9/6879 incl. `ValidateGate_ExecutesTheRealAggregateAndRejectsEveryInvalidDependencyAsync` (30 s workflow timeout), `DeletedWorkspaceRoot_RetiresSessionAndRaisesClosedOnce`; 8 pass alone.
- reloc-b (PR 2144): Failed 22 fork-storage cases (ForkNativeOwnership, ForkStorageBoundary, ForkNativeCopyAdmission, ForkNativeBuildOutputAdmission, WorkspaceForkApply, WorkspaceForkDocumentEncoding, WorkspaceForkSourceLifecycle); 74/74 alone.
- file-watcher (PR 2146): Failed 20 deadline/process tests (e.g. `RestoreGraph_PhaseExpiresBeforeGraphWorkStarts_ReportsTimeout`, `NativeBuildHost_LateChild_...`, `VerifyRelease_*` x5); 45/45 alone.
- shared-expr (PR 2142): `NativeCacheVerification_RejectsAnInvalidSignatureAsync` (pwsh probe 17 s under load, 627 ms alone).
- Overlaps: `timing-test-reaudit-wave` (other deadline sites), `missing-root-retirement-provider-dispose-timeout-flake` (ProviderDispose_* only), `test-temp-root-reap-tests-delete-live-sibling-runs` (cross-host directory deletion, a separate cause of sub-second failures).

## Context

- Size L: the families span fork storage, native build host and script runners; split per family when picked up.
