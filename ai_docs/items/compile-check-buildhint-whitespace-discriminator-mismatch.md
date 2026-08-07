# compile-check-buildhint-whitespace-discriminator-mismatch — align BuildHint's zero-projects discriminator with ProjectFilterHelper's whitespace semantics

**row:** `compile-check-buildhint-whitespace-discriminator-mismatch` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:272` (`BuildHint`'s `zeroProjectsHint` discriminator)
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:119` (`ComputeRequestedScope`, already uses `IsNullOrWhiteSpace`)
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:313` (`ResolveProjectScope`, already uses `IsNullOrWhiteSpace`)
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs`

## Acceptance

- [ ] `BuildHint`'s `zeroProjectsHint` discriminator at line 272 uses `string.IsNullOrWhiteSpace(projectFilter)` instead of `projectFilter is null`, matching every other guard in the file.
- [ ] A test covers a whitespace-only `projectFilter` (e.g. `"   "`) with zero resolved projects and asserts the "did not resolve to any loaded workspace document" / workspace-reload hint wording fires, not the wrong "did not match any project" text.

## Evidence

- Traced during code-quality review of PR #1191 (`project-filter-helper-whitespace-normalize`): that PR removed `CompileCheckService.cs:42`'s whitespace-to-null coercion — the only thing that previously made line 272's `projectFilter is null` behave equivalently to `IsNullOrWhiteSpace`. Confirmed still unresolved as of PR #1194 (`compile-check-restorehint-empty-not-null`, merged 2026-08-07): that PR touched `BuildHint`'s final join/null logic but not the `zeroProjectsHint` discriminator at line 272, which still reads `projectFilter is null`.

## Context

Originally flagged as an amend target for the (now-closed) `compile-check-restorehint-empty-not-null` row, but that row shipped (PR #1194) without touching this specific discriminator — filed as its own row since there is no longer an open row to amend.
