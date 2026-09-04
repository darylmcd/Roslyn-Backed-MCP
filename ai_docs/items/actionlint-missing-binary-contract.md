# actionlint-missing-binary-contract — Lock post-extraction binary check

**row:** `actionlint-missing-binary-contract` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-actionlint.ps1:138-139`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] A `PwshScriptRunner` regression supplies an archive that extracts successfully without the expected binary and asserts the exact fail-closed diagnostic.

## Evidence

The successful-extraction/missing-binary branch is currently untested.

## Context

Split from `verify-actionlint-chmod-and-throw-branch-coverage`; this child owns only the post-extraction binary regression shape.
