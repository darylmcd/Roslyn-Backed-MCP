# surface-test-audit-artifact-gate-and-scorecard-staleness — true up the audit-artifact docs, exempt the checkpoint file, and detect a frozen scorecard

**row:** `surface-test-audit-artifact-gate-and-scorecard-staleness` · **pri:** `Medium` · **size:** `M`

## Anchors

- `ai_docs/audit-reports/README.md:16` (asserts the scorecard is gitignored / not version-controlled)
- `skills/mcp-server-surface-test/prompts/phases/output-and-close.md:259` (run-end HARD GATE exemption list)
- `skills/mcp-server-surface-test/prompts/full.md:39` (mandates persisting `<audited-repo-root>/.audit-state.json`)
- `eng/aggregate-promotion-scorecards.ps1` (no staleness comparison)
- `.gitignore:548-556` (comment promising a lost refresh shows up as an absent diff)
- `tests/RoslynMcp.Tests/Skills/McpServerSurfaceTestSkillTests.cs:195`
- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs:5`

## Acceptance

- [ ] `ai_docs/audit-reports/README.md:16` no longer calls `audit-reports/_latest-promotion-scorecard.json` "a gitignored, generated artifact — not version-controlled"; it describes the file as tracked (via the single `.gitignore` negation) and links it relatively instead of citing it by path-only.
- [ ] `.audit-state.json` cannot self-report as a leak: either it is added to the run-end gate's exemption list at `output-and-close.md:259`, or the skill requires deleting it at closure. (It is written to the audited repo root after every phase boundary and is NOT gitignored, so a resumable run currently leaves an untracked entry in front of a gate that files any such entry as a P1 `audit-prompt-leak`.)
- [ ] A frozen canonical scorecard is detected rather than silent: `eng/aggregate-promotion-scorecards.ps1` (or a test) compares the snapshot's `serverVersion` / `generatedAt` against the current build and fails or warns when they diverge — making the `.gitignore` comment's promise ("a lost refresh shows up as an absent diff instead of failing silently") actually true.
- [ ] `McpServerSurfaceTestSkillTests.cs:195` asserts the `PRIMARY checkout` phrasing appears within each of the two anchored canonical-path sections, rather than `Regex.Matches(...).Count >= 2` over the whole file (a global phrase count does not prove "at BOTH artifact-write sites", and the current count sits exactly at the floor).
- [ ] `GitFixtureRunner`'s doc comment covers read-only queries against the real repository root, not just "tests that build a small on-disk git repo around the SampleSolution fixture".

## Evidence

- All five verified against `main` after PR #1202 (`promotion-tier-scorecard-refresh`) merged: `git check-ignore -v audit-reports/_latest-promotion-scorecard.json` exits 1 and `git ls-files audit-reports/` prints exactly that path (so it IS tracked, contradicting the README); `git check-ignore .audit-state.json` exits 1 (not ignored) and `rg audit-state` over `skills/mcp-server-surface-test/**` matches only `prompts/full.md` (no phase file or SKILL.md deletes it); `rg 'serverVersion|generatedAt|stale|MaxAge' eng/aggregate-promotion-scorecards.ps1` returns only comments and the writer, no comparison; and the tracked snapshot still reads `generatedAt: 2026-05-16T06:25:47Z` / `serverVersion: 1.38.1` against a v2.3.8 server.

## Context

PR #1202 delivered the **durability prerequisite** for promotion-tier work — it made the canonical scorecard git-tracked and pinned the surface-test artifact writes to the primary checkout — but it did NOT refresh the scorecard contents. Acceptance bullet 1 of `promotion-tier-execution-batch` (re-run the scorecard against the current v2.3.x surface) therefore remains open, and that row stays open with it.

These five items are the debris that change left behind: two docs that describe the pre-change world, one checkpoint file that the newly-tightened leak gate would flag, one unimplemented promise in a `.gitignore` comment, and two test-assertion hygiene issues from the same PR. Consolidated into one row because they all sit in the surface-test / audit-artifact surface and would otherwise be five colliding PRs against the same three files.

## Notes

- Do NOT fold the actual scorecard refresh into this row — that belongs to `promotion-tier-execution-batch`, whose own `items/` file carries it.
- The `skills/promote-tier/` anchor cited by `promotion-tier-execution-batch` does not exist; the maintainer skill lives at `.claude/skills/promote-tier/`. Fix that anchor when that row is next opened.
