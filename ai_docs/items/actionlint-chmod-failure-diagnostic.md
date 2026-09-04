# actionlint-chmod-failure-diagnostic — Surface chmod failure precisely

**row:** `actionlint-chmod-failure-diagnostic` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-actionlint.ps1:111`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] A non-zero `chmod +x` result fails with the exact bounded message `failed to mark actionlint executable` instead of being discarded or surfacing as a later launch failure.

## Evidence

`verify-actionlint.ps1` redirects chmod stderr to null and does not inspect `$LASTEXITCODE`.

## Context

Split from `verify-actionlint-chmod-and-throw-branch-coverage`; this child owns only the chmod diagnostic regression shape.
