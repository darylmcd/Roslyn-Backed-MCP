---
category: Fixed
---

- **Fixed:** `/release-cut` Step 6 now invokes the maintainer-local `/update` override at `.claude/skills/update/` instead of the shipped `/roslyn-mcp:update`. The shipped skill's Layer 2 path falls back to chat-only instructions ("run `/plugin marketplace update` then `/plugin install`") because `verify-skills-are-generic.ps1` blocks repo-specific `eng/` references; the override calls `eng/update-claude-plugin.ps1` directly to git-pull the marketplace clone, re-sync the plugin cache, prune the old version directory, and update `installed_plugins.json`. Caught after v1.34.2's release-cut bumped successfully but Step 6 produced unexecutable instructions instead of running the actual layer-2 update. Closes the implicit follow-up to `release-cut-atomic-skill-bump-ship-tag-reinstall`.
