---
category: Added
---

- **Added:** PreToolUse guard in `hooks/hooks.json` that blocks `Edit`/`Write`/`MultiEdit` on release-managed files (`Directory.Build.props`, `BannedSymbols.txt`, the 5 version files, `.claude-plugin/{plugin,marketplace}.json`, `hooks/hooks.json`, `eng/verify-version-drift.ps1`, `eng/verify-skills-are-generic.ps1`) unless the agent's rationale contains `ack: release-managed`. Documented in `ai_docs/workflow.md`. Closes `pretooluse-block-release-critical-file-edits`.
