---
category: Changed
---

- **Changed:** Changelog workflow moves to a `changelog.d/` fragment-file pattern (towncrier-style) — every PR writes `changelog.d/<row-id>.md` with YAML frontmatter (`category`) + a single bullet body instead of appending to `CHANGELOG.md` under `## [Unreleased]`, eliminating the merge-conflict hotspot that bit 50% of parallel subagents in 2026-04-17 pass-1. Release-cut (`/bump`) consumes fragments, groups them by category into the new `## [X.Y.Z]` section, and `git rm`s the consumed fragments in the same commit. `.claude/skills/draft-changelog-entry/SKILL.md` and `.claude/skills/bump/SKILL.md` updated accordingly (`parallel-pr-changelog-append-friction`).
