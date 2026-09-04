# actionlint-extraction-failure-contract — Lock tar extraction failure

**row:** `actionlint-extraction-failure-contract` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-actionlint.ps1:123-124`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] A `PwshScriptRunner` regression injects a failing tar extraction and asserts the exact fail-closed diagnostic without reaching actionlint execution.

## Evidence

The extraction non-zero exit branch is currently untested.

## Context

Split from `verify-actionlint-chmod-and-throw-branch-coverage`; this child owns only the extraction-failure regression shape.
