# fork-native-tests-red-in-isolation-on-base — Re-derive the fork/native-build tests that failed alone on base

**row:** `fork-native-tests-red-in-isolation-on-base` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkNativeOwnershipTests.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkStorageBoundaryTests.cs` (also ForkRetainedSourceDeletionTests and the admission classes named in the iface-projector report)
- `ai_docs/known-flakes.md`

## Acceptance

- [ ] Run the 35 fork/native/admission methods from the iface-projector report alone on current main, on a host with free system-drive space, three times; record pass/fail per method.
- [ ] Every method that still fails alone gets its root cause fixed in this row or its own row with the error quoted; none is retried green or registered as a flake without base-commit proof.

## Evidence

- iface-projector report (PR 2147): filter-less run Failed 68 (35 methods, all Workspace fork / native build host / retained-fork storage / admission families). Base worktree at aafbd01a, those 35 methods alone: "Failed: 29, Passed: 106, Total: 135" (17 methods fail on base); the other 18 pass alone on the PR tree (39/39).
- Confound: the same day iface-extraction-b recorded C: at 444 MB free with IOException "not enough space on the disk" in fork tests, so the base failures may be environmental. The disk was later cleared (operator); the acceptance re-runs first.
