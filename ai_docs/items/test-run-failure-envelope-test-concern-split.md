# test-run-failure-envelope-test-concern-split — Split the test-run failure-envelope suite by concern

**row:** `test-run-failure-envelope-test-concern-split` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`
- `tests/RoslynMcp.Tests/TestRunFailureClassificationTests.cs` — add focused classification coverage.
- `tests/RoslynMcp.Tests/TestRunPagingContractTests.cs` — add focused paging coverage.

## Acceptance

- [ ] Separate timeout/error classification and paging/parser contracts into focused suites without duplicating fixtures.
- [ ] Preserve every existing assertion and test identity intent.
- [ ] The three focused suites run together with unchanged coverage and no shared mutable state.

## Evidence

- After moving new public-projection tests out, `TestRunFailureEnvelopeTests.cs` remains about 980 lines and mixes process lifecycle, parsing, paging, and transport concerns.
