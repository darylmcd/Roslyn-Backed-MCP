# test-fake-undo-duplicate-doubles — Promote the duplicate private FakeUndo IUndoService test double to a shared helper

**row:** `test-fake-undo-duplicate-doubles` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs:490`
- `tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs:319`

## Acceptance

- [ ] One shared FakeUndo under the tests helpers folder replaces both copies; no assertion changes.

## Evidence

- Copy-paste duplication; static finding.
