---
category: Fixed
---

- **Fixed:** audit-artifact docs now correctly describe the canonical promotion scorecard as git-tracked (not gitignored) with a working relative link; the surface-test run-end clean gate no longer files `.audit-state.json` as an `audit-prompt-leak` P1 against its own required checkpoint write, and deletes it at completed-run closure instead of leaving it behind; `eng/aggregate-promotion-scorecards.ps1` now flags scorecards whose `serverVersion`/`generatedAt` have drifted from the current build (`scorecardStaleness` + `summary.staleScorecardCount`, warn-only on stderr so stdout stays pure JSON), making the frozen-refresh promise in `.gitignore`'s comment actually true; and two surface-test regression assertions were tightened (per-section `PRIMARY checkout` phrasing checks instead of a whole-file occurrence count, and `GitFixtureRunner`'s doc comment now covers its read-only real-repo-root usage).
