# analyzer-shadow-owner-startup-sweep-race — Preserve analyzer owner initialization during cleanup

**row:** `analyzer-shadow-owner-startup-sweep-race` · **pri:** `High` · **size:** `M`

## Anchors

- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:42-43 (process directory and owner-file initialization)
- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:81,118 (sweep start before owner initialization)
- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:191-194,242-245 (missing-owner path and empty parent cleanup)
- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs (ordinary Retarget workspace-parent path and ShadowCopy recursive-directory materialization)
- tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs (existing ownership and stale-child contracts)
- (new) tests/RoslynMcp.Tests/AnalyzerShadowStartupSweepRegressionTests.cs
- (new) tests/RoslynMcp.Tests/AnalyzerShadowCreatorOwnershipTests.cs

## Acceptance

- [ ] Reproduce the initialization/sweep interleaving deterministically on unchanged base production in a private owned fixture; distinguish source reasoning from actual reproduced evidence and identify the first hosted failure.
- [ ] Protect both process and workspace parent materialization from every same-mechanism empty-parent cleanup path, including sibling callers/processes. Preserve stale CHILD lease sweeping and its existing age/active-lease policy.
- [ ] Prune empty parents only with established released ownership or proven creator absence. Retain live, inaccessible, malformed and legacy unknown-creator empty parents conservatively; accept this bounded best-effort parent-housekeeping change without suppressing stale-child cleanup.
- [ ] Observe old-code failure and repaired success without retries, sleeps as synchronization, deadline changes, broad refusal, swallowed errors, weakened tests or global cleanup. Exercise actual creation, sweep and owner-file opening behavior.
- [ ] Require cold plan/implementation review, complete affected lifecycle/loader test classes and exact-head hosted checks. Ship the independently correct prerequisite separately from the frozen nullable slice, then requalify its integration.

## Scope

- One behavior source file and three edited/new test files. ExpandedSurfaceIntegrationTests.cs:29 is an unchanged hosted witness, not an edit target; its entire class remains required validation alongside all affected classes in the corrected plan. No validation coverage is removed by classifying it as evidence.

## Evidence

- PR1854 d8a709921b5aa4a6841f6ee093fe28d777f4c1d5 run40412863293 Windows4 job121887797301 failed741passed/600failed/2skipped. ClassInit DirectoryNotFoundException at AnalyzerReferenceIsolation owner-file open repeats through shared Lazy initialization.
- Full Windows4 failed-step log is now retained. First reported failure at22:33:15.903Z is ChangeSignatureConstructorsTests.ChangeSignaturePreview_PrimaryConstructor_RefusesWithSpecificMessage; stack opens the missing owner file at AnalyzerReferenceIsolation.cs:43 through lazy creation and Retarget line118. ExpandedSurface is a later cascade witness. The deletion actor still requires deterministic base proof.
- Source permits sweep startup before process owner initialization and empty-parent deletion after seeing no marker. This matches the exception but deterministic base reproduction and complete causal attribution remain pending. No environment/pre-existing runtime classification, nullable-source regression attribution or unchanged CI rerun claimed.
- Separate analyzer-shadow-sweep-path-authority row owns reparse traversal, not this startup ordering mechanism. Nullable code remains frozen in draft1854; its local449-class-cohort and28-native-case passes do not override the hosted failure.
