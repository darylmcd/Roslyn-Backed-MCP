# deep-review-shape-list-single-source — single-source the deep-review recognized-artifact-shape list

**row:** `deep-review-shape-list-single-source` · **pri:** `Low` · **size:** `M`

## Anchors

- `eng/stage-review-inbox.ps1:7` (hardcodes a shape count in `.DESCRIPTION`)
- `eng/stage-review-inbox.ps1:27-35` (the canonical `$filePatterns` / Recognized-shapes block)
- `eng/process-audit-reports.ps1:11-12`
- `ai_docs/procedures/deep-review-backlog-intake.md:18`
- `ai_docs/procedures/deep-review-command-reference.md:13`
- `ai_docs/procedures/deep-review-command-reference.md:31`
- `.claude/skills/backlog-intake/SKILL.md:50`
- `.claude/agents/backlog-intake-extractor.md:26`

## Acceptance

- [ ] The satellite docs/skill/agent reference `eng/stage-review-inbox.ps1`'s Recognized-shapes block (or one shared doc) rather than each re-listing the glob set, so adding a pattern is a one-file edit.
- [ ] `eng/stage-review-inbox.ps1:7` no longer hardcodes a shape count ("the three ... shapes") — this was already stale before PR #1146 (four) and remains stale after (five).

## Evidence

- Code-quality review of PR #1146 (`stage-review-inbox-multisession-retro-glob-miss`): the diff added `*_roslyn-mcp-multisession-retro.md` to `$filePatterns` and the script docstring, but 6 other sites enumerate the same glob set verbatim and were left at the old list — `eng/process-audit-reports.ps1:11-12` documents exactly what the script it wraps discovers and now understates it. This duplication is why the original glob miss survived three retro cycles.

## Context

Spin-off from the `stage-review-inbox-multisession-retro-glob-miss` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1146).
