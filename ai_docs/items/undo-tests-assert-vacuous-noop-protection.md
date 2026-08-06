# undo-tests-assert-vacuous-noop-protection — byte-fidelity undo tests must assert the apply actually mutated the file

**row:** `undo-tests-assert-vacuous-noop-protection` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs:65`
- `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs:152`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs:568`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs:75` (existing sanity-assert pattern to mirror)

## Acceptance

- [ ] Each of the three tests asserts, between apply and revert, that the on-disk bytes differ from the captured `originalBytes` (mirroring `EditorConfigServiceTests.cs:75-76`'s existing `StringAssert` sanity check).
- [ ] `EditUndoIntegrationTests.cs:76-79` reuses the existing private `AppendCommentEdit` helper (line 317) instead of re-inlining its body.

## Evidence

- Code-quality review of PR #1144 (`direct-mutation-undo-byte-fidelity`): three of the four new byte-fidelity tests assert only `result.Success` then byte-equality after revert, never that the file actually changed between apply and revert — a future regression that turns the mutation into a silent no-op would leave these tests green (vacuous pass) on the critical undo path.

## Context

Spin-off from the `direct-mutation-undo-byte-fidelity` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1144).
