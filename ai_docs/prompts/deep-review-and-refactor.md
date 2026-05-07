# Deep Code Review & Refactor Agent Prompt — moved

<!-- purpose: Pointer to the prompt's new home. The 955-line living prompt now ships with the plugin under skills/audit-deep/prompts/, so consumers do not need to maintain a per-repo copy. -->

> **This file is a pointer.** The prompt body has moved into the `roslyn-mcp` plugin's `audit-deep` skill so it ships with the plugin and stays in sync with the live MCP catalog without per-repo maintenance.

## New canonical locations

- **Single canonical run (full-surface audit + Phase 6 refactor on disposable worktree + promotion scorecard):** [`skills/audit-deep/prompts/prompt.md`](../../skills/audit-deep/prompts/prompt.md)

## How to invoke

From any Claude Code session with the `roslyn-mcp` plugin installed:

```
/roslyn-mcp:audit-deep                       # single canonical run (no modes)
/roslyn-mcp:audit-deep --no-worktree         # degraded mode for CI environments without worktree access
```

The skill's `SKILL.md` resolves the requested mode to one of the three prompt files above and runs it verbatim. Like the original prompt, the skill halts immediately if the Roslyn MCP server is not callable — there is no generic non-MCP fallback.

## Why the move

- **Plugin consumers** no longer need to vendor an 872-line prompt into every C# repo and keep it current. The plugin update mechanism handles drift.
- **Mode separation** — the historical single-file prompt embedded mode behavior in prose; the three-file split makes each mode's overrides explicit and editable independently.
- **Tier promotion** — `/release-cut`'s promotion gate consumes `_latest-promotion-scorecard.json`. Shipping the prompt with the plugin keeps the scorecard schema and the gate in lockstep.

## Cross-references that still resolve

This file remains as a redirect target for the in-repo cross-links that the original prompt and surrounding documentation built up over time:

- `ai_docs/audit-reports/README.md` — points readers at this file.
- `ai_docs/audit-reports/deep-review-session-checklist.md` — operator worksheet that pairs with the prompt.
- `ai_docs/procedures/deep-review-program.md` — multi-repo batch coordinator.
- `ai_docs/procedures/deep-review-backlog-intake.md` — downstream consumer of the prompt's audit reports.
- `tests/RoslynMcp.Tests/PromptSmokeTests.cs` — references the Phase 16 invocation shape.
- `tests/RoslynMcp.Tests/ServerHeartbeatTests.cs` — references the Phase -1 hard gate.

Each of those still talks about "the prompt" conceptually; for the actual phase-by-phase content, follow the new canonical locations above.
