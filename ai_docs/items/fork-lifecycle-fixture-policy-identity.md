# fork-lifecycle-fixture-policy-identity — Compose fork lifecycle fixtures under one exact owned policy

**row:** `fork-lifecycle-fixture-policy-identity` · **pri:** `High` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/SolutionFileMutationEscapingLifecycleTests.cs:44`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.ForkInvocation.cs:10`

## Acceptance

- [ ] Use the same policy instance for owned manager, test runner and fork service.
- [ ] Discover actual native configuration grants and preserve required policy identity refusal.
- [ ] Exercise full source/fork/undo and text-solution native stage positives past operation admission.
- [ ] Migrate all shared fixture factory callers explicitly; no default shim or guard bypass.

## Evidence

- Independent full-diff source review rederived this defect in the current retained restore implementation. Required fix in this PR; this row does not authorize deferral.

2026-10-23 additional full source review: Workspace/ForkFailureRedactionTests.cs actual restore35 and LoadWithProperties38 omit OwnedNativeLoadContext properties; NativeOutputProducerObservationTests.cs ProbeCore31-33 uses empty globals for CaptureToolsVersion/PrepareOutputs unlike sibling4 actual fixtures. Qualify actors with the same explicit owned native properties, not ambient import grants; preserve all producer implementation/context/parameter/hash/denial/factory controls.
