---
category: Added
---

- **Added:** `/release-cut` skill at `.claude/skills/release-cut/SKILL.md` — single-invocation release pipeline (preflight → bump → verify → ship → tag → reinstall-both-layers) that delegates to `/bump`, `/ship`, and `/roslyn-mcp:update` with checkpointed step output. Closes the 2026-04-17 + 2026-04-18 two-session Layer-2-gap pattern where `dotnet publish -p:ReinstallTool=true` alone left the plugin marketplace cache stale; shell-quoting for `-p:ReinstallTool=true` (dash form required on bash-on-Windows) pinned inside the skill (`release-cut-atomic-skill-bump-ship-tag-reinstall`).
