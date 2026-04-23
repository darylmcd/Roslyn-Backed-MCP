---
category: Changed
---

- **Changed:** `/roslyn-mcp:update` skill gains a repo-local maintainer override at `.claude/skills/update/SKILL.md` that leads Layer 2 with the agent-executable `pwsh eng/update-claude-plugin.ps1` updater and keeps `/plugin marketplace update` + `/plugin install` as a fallback. Surfaced during the v1.29.0 release-cut (PR #377) where the Claude Code client responded with `/plugin isn't available in this environment`, leaving Layer 1 updated but Layer 2 stuck at 1.28.1 in the plugin cache — the bundled PowerShell script already replicated the slash-command flow (pull marketplace clone, re-sync cache from git-tracked files, prune stale versions, update `installed_plugins.json` + `known_marketplaces.json`), but the shipped skill couldn't reference repo-specific `eng/` paths under `verify-skills-are-generic.ps1`. The shipped generic skill keeps its `/plugin`-only guidance plus a pointer to the `.claude/skills/` override for maintainers in-repo.
