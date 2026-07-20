# legacy-sln-slnx-parity-drift — retire the legacy `Roslyn-Backed-MCP.sln` (or gate it against `RoslynMcp.slnx`)

**row:** `legacy-sln-slnx-parity-drift` · **pri:** `Low` · **size:** `S`

## Anchors

- `Roslyn-Backed-MCP.sln` — legacy VS solution, 8 project entries, last touched 2026-04-17 (`be2bc4ea`); the file to delete
- `docs/setup.md:23` — the only live reference to it (labels it "Legacy Visual Studio solution"); the line to drop

Read-only context (not edit targets): `RoslynMcp.slnx` (primary solution, 5 project entries), `eng/verify-release.ps1:20` and `.github/workflows/ci.yml:175` (both build/scan the `.slnx`, never the `.sln`).

## Acceptance

Preferred — delete:

- [ ] `Roslyn-Backed-MCP.sln` is removed from the repo.
- [ ] `docs/setup.md:23` row is dropped (or rewritten to state `.slnx` is the only solution file).
- [ ] `dotnet build RoslynMcp.slnx` and the full test suite still pass.

Fallback (only if a live consumer for the `.sln` is identified — record who/what in this file first):

- [ ] A test or `eng/` check asserts the two solution files list the same `src/`, `analyzers/`, and `tests/` project paths, failing when one gains a project the other lacks.
- [ ] The check is wired into CI on the same path as the other repo-shape gates.

## Evidence

- Session 2026-07-19 (`.slnx` loader-support question): the two solution files have already drifted — the `.sln` carries `samples/**` project entries (`SampleSolution`, `SampleApp`, `SampleLib`) that `RoslynMcp.slnx` does not, and nothing in CI or `eng/` enforces parity. A project added to `.slnx` would be silently missing from the `.sln` indefinitely.
- No live consumer found: `rg -F "Roslyn-Backed-MCP.sln"` outside `review-inbox/` archives and `ai_docs/plans/` returns only `docs/setup.md:23`.

## Context

Dual-maintenance hazard, not a correctness bug — no runtime or published-artifact impact (the `.sln` is dev-time only and is not part of the `roslyn-mcp` package surface, so removing it is not a breaking change for consumers).

Deletion is the recommended arm: `.slnx` is the dogfooded primary (CI, `verify-release`, all sample fixtures), the repo pins SDK `10.0.100`/`latestFeature`, and VS 17.13+ opens `.slnx` natively — so the "legacy Visual Studio" justification for keeping the `.sln` no longer holds. Low priority; fold into the next docs or repo-shape pass.
