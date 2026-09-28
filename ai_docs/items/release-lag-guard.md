# release-lag-guard — Warn when correctness fixes sit unreleased

**row:** `release-lag-guard` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-changelog-fragments.ps1`
- `docs/release-policy.md`
- (new) `tests/RoslynMcp.Tests/ReleaseLagGuardTests.cs`

## Acceptance

- [ ] The fragment verifier reports every `category: Fixed` fragment in `changelog.d/` whose add commit is older than a threshold (default 7 days, overridable through an env var, not a literal in the check) as a non-failing warning wherever the verifier already runs: the PR CI legs and the standalone release verifier.
- [ ] The age lookup uses one git invocation for all fragments, so the Codex publication-boundary hook that runs the same verifier stays well inside its 30 s cap; the AGENTS.md Validation runtime row is re-measured.
- [ ] The threshold, the env var and the warning text are documented in `docs/release-policy.md`.
- [ ] Tests cover a fresh Fixed fragment (no warning), a stale one (warning), a stale non-Fixed one (no warning) and the env override.

## Evidence

- At `19ccd61b`: 26 Fixed fragments are unreleased, the oldest added 2026-09-19, while consumers stay pinned to 4.2.1 (`.claude-plugin/mcp.json:6`; last release `4eba2601`, 2026-09-18). Nothing surfaced the lag until the 2026-09-27 retrospective counted it (`fixes-stranded-unreleased-4-2-1-pin`).

## Context

- Companion to `v5-major-release-contract-prereqs`, which carries the release decision itself. This row only makes the lag visible; it never blocks a merge.
