---
category: Maintenance
---

- **Maintenance:** Recorded the CI topology split in `ai_docs/prompts/backlog-sweep-addenda.md`. `ci_equivalent` describes the code-PR shape, but `eng/resolve-ci-topology.ps1` routes a PR whose changed paths are all `^(.*\.md|ai_docs/.*\.json)$` — excluding the behavior-bearing carve-out of `CHANGELOG.md` and anything under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, `.github/prompts/` — to a two-leg Linux docs matrix that skips `verify-changed-format`, `verify-nuget-audit` and the `sdk-floor` job, and forces `-TestShardOnly` on every `verify-release.ps1` leg. A docs-only initiative following `ci_equivalent` verbatim therefore runs two scripts CI will skip; the addenda now states that over-validating is the intended default and that a markdown-shaped change touching a skill, agent, prompt or the changelog is still a code PR.
