# client-root-path-validator-complexity-extraction — Simplify sanctioned-root matching

**row:** `client-root-path-validator-complexity-extraction` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:119-163`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] `IsPathUnderAnyRoot` and every new or changed helper measure cyclomatic complexity at or below 8.
- [ ] Allowed-root enumeration is lazy and preserves exact-path, separator-bounded child, case-insensitive, and trailing-separator behavior.
- [ ] Parent widening permits exactly one non-drive level; sibling/prefix-trap/grandparent paths and drive-root widening remain rejected.
- [ ] The refactor adds no root-list materialization and no filesystem I/O.

## Evidence

- Read-side Roslyn metrics on 2026-07-17 measured `IsPathUnderAnyRoot` at CC 11.

## Dependencies

- None.
