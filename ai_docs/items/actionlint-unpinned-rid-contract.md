# actionlint-unpinned-rid-contract — Lock unpinned-RID refusal

**row:** `actionlint-unpinned-rid-contract` · **pri:** `Medium` · **size:** `S` · **deps:** `actionlint-unsupported-platform-contract`

## Anchors

- `eng/verify-actionlint.ps1:75-77`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] A `PwshScriptRunner` regression drives an unsupported runtime identifier and asserts the exact fail-closed diagnostic without network access.

## Evidence

The RID pin allowlist refusal branch is currently untested.

## Context

Split from `verify-actionlint-chmod-and-throw-branch-coverage`; this child owns only the unpinned-RID regression shape.


## 2026-09-04 re-vet addendum

Current detection maps every Windows architecture to `win-x64`, every macOS architecture to `osx-arm64`, and every non-`aarch64` Linux architecture to `linux-x64`. This makes the allowlist refusal unreachable from real platform detection.

### Additional acceptance

- [ ] Derive the RID from both OS and process architecture; support only explicitly pinned combinations.
- [ ] Any real but unpinned OS/architecture pair fails before cache, filesystem artifact, or network work with the exact bounded diagnostic.
- [ ] Existing pinned Windows, macOS, and Linux combinations retain their expected RID and checksum selection.

### Regression

Inject an otherwise recognized OS with an unsupported architecture and assert the unpinned-RID diagnostic, no download attempt, and no artifact creation.


> Anchor correction (2026-09-04): supersedes the stale `eng/verify-actionlint.ps1:75-77` anchor. Runtime RID selection is at `eng/verify-actionlint.ps1:60-68`; the allowlist refusal is at `:99-102` on the reconciled generation-one base.
