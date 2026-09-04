# verify-actionlint-chmod-and-throw-branch-coverage — `verify-actionlint.ps1`: chmod failure is silently swallowed and several defensive throw branches are untested

**row:** `verify-actionlint-chmod-and-throw-branch-coverage` · **pri:** `Low` · **size:** `S`

## Anchors

- `eng/verify-actionlint.ps1:111` (chmod)
- `eng/verify-actionlint.ps1:58` (unsupported platform)
- `eng/verify-actionlint.ps1:75-77` (unpinned RID)
- `eng/verify-actionlint.ps1:123-124` (extraction failure)
- `eng/verify-actionlint.ps1:138-139` (missing binary post-extraction)
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] `chmod +x $binaryPath` failure (non-zero exit) surfaces a specific "failed to mark actionlint executable" error instead of being discarded via `2>$null`.
- [ ] Each remaining defensive throw branch (unsupported platform, unpinned RID, tar-extraction failure, extraction-produced-no-binary) has at least one `PwshScriptRunner`-based regression test, or is documented as intentionally unreachable in this repo's CI matrix (only `win-x64`/`linux-x64` actually run).

## Evidence

Two cold `implementation-reviewer` passes on `ci-actionlint-pinned-gate` (PR #1444, merged), both cycles: `eng/verify-actionlint.ps1:111` (`& chmod +x $binaryPath 2>$null`) discards chmod failures — a genuinely failed chmod would only surface indirectly via a less-specific later error. `ActionlintGateContractTests.cs` covers hash-mismatch (cached + override) and the cold-download/cache-hit happy path (Network-gated), but not the unsupported-platform, unknown-RID, empty-workflow-dir, or extraction-produced-no-binary throw paths.

## Context

Both LOW-severity findings against the same new file, consolidated into one row per doc-audit closeout convention.
