---
category: Added
---

- **Added:** PostToolUse hook in `hooks/hooks.json` that runs `eng/verify-skills-are-generic.ps1` on every `skills/**/SKILL.md` edit, surfacing shipped-skill genericity violations the same turn instead of waiting for the CI gate. Dispatches through a thin stdin-JSON wrapper at `eng/verify-skills-on-edit.ps1` to avoid brittle inline shell quoting. Closes `posttool-verify-skills-on-shipped-skill-edits`.
