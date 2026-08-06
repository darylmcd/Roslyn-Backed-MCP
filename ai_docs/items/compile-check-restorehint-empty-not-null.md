# compile-check-restorehint-empty-not-null — compile_check restoreHint returns "" instead of null

**row:** `compile-check-restorehint-empty-not-null` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:236` (`BuildHint` `<summary>` claims null when neither hint applies)
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:269` (`string.Join(" ", …)` over a filtered-empty sequence returns `string.Empty`)
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs:61` (vacuous `Assert.IsNotNull(result.RestoreHint)`)

## Acceptance

- [ ] `BuildHint` returns null when no hint applies (or its `<summary>` is corrected), so `restoreHint` stops serializing as an empty string on every clean `compile_check` response.
- [ ] The vacuous `Assert.IsNotNull(result.RestoreHint)` is replaced with an assertion that can fail, plus a test asserting `restoreHint` is null/absent on a clean scoped check.

## Evidence

- Code-quality review of PR #1151 (`compile-check-multi-project-fallback-structured-scope`): `BuildHint`'s `<summary>` claims null when neither hint applies, but `string.Join` over a filtered-empty sequence returns `string.Empty`, and `JsonDefaults.Indented` sets no `WhenWritingNull`, so every clean response carries `"restoreHint": ""`. The diff's own new comment documents this discrepancy instead of tracking it.

## Context

Spin-off from the `compile-check-multi-project-fallback-structured-scope` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1151).
