---
name: mcp-server-stress
description: "Maintainer-only thin alias for `/mcp-server-surface-test --output-mode=fragments`. Repo-local skill (lives under `.claude/skills/`, only discoverable from a Claude Code session whose project root is Roslyn-Backed-MCP). Invokes the canonical audit prompt at `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/prompts/full.md` with fragments-mode emission so findings land as `<audited-repo-root>/backlog.d/<finding-id>.md` files for `/backlog-intake` to consume — the maintainer's intake-pipeline destination, distinct from the consumer auto-file path to GitHub Issues. The single prompt (full.md) drives both consumer surface-test runs and maintainer stress runs; the only difference is the output-mode flag. The legacy `maintainer-overlay.md` prompt was deleted in v1.X.Y when its repo-coupled phases were folded into the canonical prompt behind auto-detection (reports path) and the `--output-mode=fragments` flag (Phase 19 destination). Use when: running an audit against a maintainer-managed repo with a `backlog.d/` ingestion pipeline. **Limitation:** the Roslyn MCP server's sanctioned-root restriction means `workspace_load` refuses paths outside the current session's repo root, so `--target=<other-repo>` does NOT work from this skill — open Claude Code in the audited repo directly and use `/mcp-server-surface-test --output-mode=fragments` from that session instead."
user-invocable: true
argument-hint: "[--no-worktree] [--single-agent]"
---

# /mcp-server-stress $ARGUMENTS

Maintainer-only repo-local alias for `/mcp-server-surface-test --output-mode=fragments`. Routes to the canonical audit prompt at `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/prompts/full.md` with fragments-mode emission so findings produce `<audited-repo-root>/backlog.d/<finding-id>.md` files for the `/backlog-intake` pipeline.

## What this skill does

Invoke `/mcp-server-surface-test --output-mode=fragments` against the current Claude Code session's repo root (the only path the Roslyn MCP server's sanctioned-root restriction permits — see *Limitations* below). Forward any additional flags (`--no-worktree`, `--single-agent`, `--quick`) verbatim.

## Why this skill exists (and why it's a thin alias)

Before v1.X.Y, `/mcp-server-stress` used a separate `maintainer-overlay.md` prompt that duplicated ~85% of `full.md` while adding three repo-coupled phases (backlog.d/ fragment emission, ai_docs/audit-reports/ report path, ai_docs/backlog.md regression cross-check). Every prompt fix had to be applied to both files, and they drifted in subtle ways.

The v1.X.Y refactor folded the maintainer-overlay's unique content into `full.md` behind:
- `--output-mode=fragments` → Phase 19 emits backlog.d/ fragments (the maintainer-overlay's Phase 19 behavior).
- Auto-detection of `ai_docs/audit-reports/` vs `audit-reports/` → reports land in the maintainer's doc-audit schema location when that schema exists, top-level otherwise.
- Auto-detection of `ai_docs/backlog.md` vs `backlog.md` for regression source → Phase 18's "prior source" probe handles both.

`/mcp-server-stress` survives as a 1-line alias because:
1. **Muscle memory.** The maintainer types `/mcp-server-stress` reflexively; renaming to `/mcp-server-surface-test --output-mode=fragments` everywhere is friction.
2. **Repo-local discoverability.** This skill is only available from Claude Code sessions rooted in Roslyn-Backed-MCP (per `.claude/skills/` discovery). The repo-local placement signals that fragments-mode is the maintainer-standard invocation.

## Invocation

The skill body delegates entirely to `/mcp-server-surface-test`. The agent should:

1. Read `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/SKILL.md` for the canonical preconditions, flag handling, and prompt-execution flow.
2. Execute that skill with `--output-mode=fragments` prepended to any `$ARGUMENTS` the operator passed.
3. The audited repo is the current Claude Code session's repo root (no `--target=<path>` — see *Limitations*).

Functionally equivalent to typing `/mcp-server-surface-test --output-mode=fragments $ARGUMENTS`.

## Limitations

- **No cross-repo `--target=<path>` flag.** The Roslyn MCP server's `workspace_load` refuses any path outside the Claude Code client's sanctioned root, which is fixed at session-start time to the directory Claude Code was opened in. To audit a different repo, **open Claude Code in that repo** and invoke `/mcp-server-surface-test --output-mode=fragments` from that session (the skill is plugin-shipped, so it's available from any repo). This is a structural limitation of the MCP root-sanction model; the cross-repo `--target=` invocation that the prior version of this skill advertised never actually worked end-to-end and has been removed.

## Hard rules (inherited from `/mcp-server-surface-test`)

- **Server-required.** No generic non-MCP fallback exists. If `mcp__roslyn__server_info` is not callable or `connection.state` is not `ready`, halt.
- **Read-only against `main`.** Phase 6 apply-mode mutations confine to the disposable worktree the prompt creates. Never push or merge from inside this skill.
- **Mutation isolation contract.** Phase 7 / 8b / 13 writes target the disposable worktree only. Run-end primary-checkout `git status` MUST be empty (Phase 0 baseline + Final surface closure step 3a hard gate).
- **No silent truncation.** Subagent phases that return `skipped-budget` / `skipped-context` / `truncated` markers are hard FAILs; orchestrator re-dispatches or records `phase-failed-budget` as P1.
- **P0 / security findings never go to GitHub Issues** under findings mode. Under fragments mode, `area: security` triggers `/backlog-intake --publish`'s refusal contract when the fragment is later published.
