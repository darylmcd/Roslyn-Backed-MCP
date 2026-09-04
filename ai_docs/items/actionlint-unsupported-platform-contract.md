# actionlint-unsupported-platform-contract — Lock unsupported-platform refusal

**row:** `actionlint-unsupported-platform-contract` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-actionlint.ps1:58`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] A `PwshScriptRunner` regression drives an unsupported platform snapshot and asserts the exact fail-closed diagnostic, or proves and documents that the branch is unreachable under the supported host contract.

## Evidence

The defensive branch is currently untested.

## Context

Split from `verify-actionlint-chmod-and-throw-branch-coverage`; this child owns only the unsupported-platform regression shape.
