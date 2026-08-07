---
category: Maintenance
---

- **Maintenance:** Single-sourced the deep-review recognized-artifact-shape list for the automation-consumed sites — `eng/process-audit-reports.ps1`, `.claude/skills/backlog-intake/SKILL.md`, and `.claude/agents/backlog-intake-extractor.md` now point at `eng/stage-review-inbox.ps1`'s canonical `Recognized shapes` block instead of re-listing the glob set, and that script's `.DESCRIPTION` no longer hardcodes a shape count. (Partial slice — 2 human-facing procedure docs remain and are tracked in a follow-up row.)
