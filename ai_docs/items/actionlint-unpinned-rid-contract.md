# actionlint-unpinned-rid-contract — Lock unpinned-RID refusal

**row:** `actionlint-unpinned-rid-contract` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-actionlint.ps1:75-77`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] A `PwshScriptRunner` regression drives an unsupported runtime identifier and asserts the exact fail-closed diagnostic without network access.

## Evidence

The RID pin allowlist refusal branch is currently untested.

## Context

Split from `verify-actionlint-chmod-and-throw-branch-coverage`; this child owns only the unpinned-RID regression shape.
