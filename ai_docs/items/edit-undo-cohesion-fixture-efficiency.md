# edit-undo-cohesion-fixture-efficiency — Own and narrow cohesion fixtures

**row:** `edit-undo-cohesion-fixture-efficiency` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/OwnedRefactoringTestBase.cs`

## Acceptance

- [ ] Give each invocation its own production service graph and fresh restored SampleLib copy; reuse the existing owned lifecycle.
- [ ] Preserve all 17 methods / 24 expanded cases, invalid-input permutations, byte/version/undo assertions, CRLF pre-load setup and transient-handle deadline.
- [ ] Verify identical runtime case identities and repeated before/after timings; run the complete local gate and hosted Windows/Linux matrix.
- [ ] Close this row only with shipped coverage and timing evidence.

## Evidence

The class loads a three-project solution for SampleLib-only edits and inherits shared mutable service ownership. Two unchanged Release baseline runs on b4149dcf passed all 24 cases in 74.680270 and 75.538838 seconds.
