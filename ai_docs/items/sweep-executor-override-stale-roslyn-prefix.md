# sweep-executor-override-stale-roslyn-prefix — sweep-executor-override-stale-roslyn-prefix

**row:** `sweep-executor-override-stale-roslyn-prefix` · **pri:** `High` · **size:** `S` · **deps:** `roslyn-hook-matchers-stale-tool-prefix`

## Anchors

- `.claude/agents/initiative-executor.md` — port the resolved-prefix contract, or delete the override.
- `.claude/settings.local.json` — drop the dead `enabledMcpjsonServers` entry.

## Acceptance

- [ ] The repo-local executor override names no hardcoded `mcp__roslyn__<tool>`; every Roslyn tool goes through a `<roslyn>` prefix resolved from the tool whose name ends in `server_info`, matching the global agent after PR #354 upstream.
- [ ] The override requires the `roslyn: <prefix> used=[tools] | unavailable (<reason>)` notes token, or the override is deleted and the global agent inherited.
- [ ] `enabledMcpjsonServers: ["roslyn"]` is removed from `.claude/settings.local.json` or a `.mcp.json` defining that server is added.
- [ ] The permission allowlist owned by `roslyn-hook-matchers-stale-tool-prefix` is left untouched (see Context).

## Evidence

- Verified 2026-09-18 against HEAD: `.claude/agents/initiative-executor.md` contains 10 occurrences of `mcp__roslyn__`, 0 of `mcp__plugin_roslyn-mcp_roslyn__`, 0 of `<roslyn>`, and 0 of `server_info` — it is the pre-fix text.
- The live plugin registers its tools as `mcp__plugin_roslyn-mcp_roslyn__*` (confirmed this session via `server_info`, which reports `resourceServerNames.canonical = "roslyn"` with `plugin:roslyn-mcp:roslyn` among its aliases).
- This repo has no `.mcp.json`, so nothing registers a bare `roslyn` server here, yet `.claude/settings.local.json:41` sets `enabledMcpjsonServers: ["roslyn"]` — dead configuration pointing at a file that does not exist.
- Upstream `~/.claude` closed the same defect twice — PR #342 (`bl-0386`, agent tool names) and PR #354 (`sweep-roslyn-tools-never-invoked`, prefix resolution) — but a repo-local override supersedes the global agent, so this repo still reproduces it.
- Scope correction 2026-09-18 (anchor-overlap check against `roslyn-hook-matchers-stale-tool-prefix`): `.claude/settings.json` is NOT wholly stale — measured 16 bare `mcp__roslyn__` entries and 6 plugin-prefixed entries, i.e. half-migrated, which is exactly the state that row already describes and owns. This row's original third acceptance bullet duplicated it and was removed; the allowlist anchor was dropped with it.

## Context

Root symptom, from the TradeWise sweep retrospective 2026-09-17 and reported in variation by other repos: across 47 agent transcripts in one sweep, zero Roslyn tool calls were made; executors silently fell back to `dotnet build`/`test` because the tool names they were told to use matched no registration. The global fixes landed 2026-09-17; this override was not swept with them.

Per `~/.claude/CLAUDE.md`, a product repo's `.claude/**` holds only repo-local overrides, so the fix belongs here rather than to the global toolset. Confirm the override still earns its existence before porting — if its only remaining delta is the stale prefix, deleting it and inheriting the fixed global agent is the smaller complete fix.
Boundary with roslyn-hook-matchers-stale-tool-prefix (deps): that row owns the .claude/settings.json permission allowlist (16 bare / 6 plugin entries) and the hooks.json matchers. This row owns only the agent override and the dead enabledMcpjsonServers entry. Do not edit the allowlist here.
