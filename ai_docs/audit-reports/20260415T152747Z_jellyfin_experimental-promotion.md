# Experimental Promotion Exercise Report — BLOCKED at Phase 0

> **Re-SUPERSEDED (2026-04-15, third pass — v1.18.2):** The v1.18.1 diagnosis below was wrong. The actual root cause for §9.1 is the CLI SDK's `QkH` REQUIRED-mode `${user_config.*}` resolver throwing `Missing required user configuration value: {K}` during plugin-config resolution — before the server process is spawned. Binary evidence: `claude.exe` at offset `125626186` contains the throw; the desktop-app loader at `app.asar` function `fRe` ~offset `2682541` uses `p = f.mcpServers ?? f`, so v1.18.1's format-split was a no-op. The v1.18.1 server-side `ReadEnv` defensive helper is unreachable in this failure path (the server never starts). **Real fix shipped in v1.18.2:** dropped the `env` block from both plugin-shipped `.mcp.json` files so `QkH` has no `${user_config.*}` references to resolve; server uses compiled-in defaults. Also removed the broken `userConfig` / `user_config` blocks from `plugin.json` and `manifest.json` (BREAKING for anyone who had configured values there — they're now inert; migrate to project-scope `.mcp.json`). See CHANGELOG `## [1.18.2]` for full reasoning. The §3.2 plugin-format contrast matrix in the retired task brief pointed in the wrong direction.

> **SUPERSEDED (2026-04-15 later the same day):** Root cause for §9.1 was the plugin-scope `.mcp.json` format, not `${user_config.*}` substitution. Fixed in **v1.18.1** — the bundled `.mcp.json` inside the plugin now uses the top-level server-name shape (`{ "roslyn": {...} }`) that Claude Code's plugin loader expects, while the repo-root file keeps the `mcpServers` wrapper for project-scope use. Companion hardening: the stdio host now routes every `ROSLYNMCP_*` env read through a defensive `ReadEnv(name)` helper that falls back to in-source defaults when a literal `${user_config.KEY}` placeholder arrives unresolved, with one stderr line per ignored placeholder. Closed `dr-9-1-high-roslyn-plugin-mcp-does-not-connect-in-audit`; updated `mcp-connection-session-resilience` to drop the substitution-pipeline narrative. See CHANGELOG `## [1.18.1]`. The §3.2 plugin-format contrast matrix in the retired brief was the decisive clue; the brief itself has been removed per archive policy (recover from git history if needed). *[Note added 2026-04-15 pass 3: this diagnosis was wrong. See the v1.18.2 banner above.]*

> **Status: BLOCKED (2026-04-15).** Exercise could not enter Phase 0 because the `roslyn` MCP server never connected in the auditing Claude Code session. Every subsequent phase depends on tools (`server_info`, `workspace_load`, `workspace_status`, `project_graph`, etc.) that are only callable via that server. No experimental tools were exercised. All findings below relate to **auditing-harness defects** (plugin loader, skill graceful-degradation, false-signal heuristics) — not to the experimental tool surface itself.
>
> **Quick triage index (by severity):**
> - **HIGH** → §9.1 (roslyn plugin-provided MCP doesn't spawn in fresh sessions with no local `.mcp.json`; all other plugins in the same cohort load) — **FIXED in v1.18.1**, see supersede note above
> - **MEDIUM** → §9.2 (`roslyn-mcp:analyze` skill has no graceful-degradation path when the server isn't connected), §9.3 (Claude Code's deferred-tool advertisement is a misleading connection signal)
> - **LOW** → §9.4 (the `mcp-logs-<server>/` cache-dir heuristic is unreliable — documented so future diagnosticians don't repeat the mistake)
>
> **Next-agent instructions:** §9.1 closed in v1.18.1 (see top supersede note). §9.2 — `roslyn-mcp:*` skills still assume the server is connected; graceful-degradation work tracked under the P4 row `dr-9-2-medium-and-related-skills-have-no-graceful-degra`.

## 1. Header
- **Date:** 2026-04-15 (UTC 15:27)
- **Audited solution:** `C:\Code-Repo\jellyfin\Jellyfin.sln` — **not loaded**
- **Audited revision:** jellyfin `master` at `fb33b72` (`Fix in-process restart (#16482)`)
- **Entrypoint loaded:** *none* (workspace_load never callable)
- **Audit mode:** n/a (exercise blocked before mode selection)
- **Isolation:** auditing session cwd `C:\Code-Repo\jellyfin` (main repo, not a worktree — `git worktree list` shows only main)
- **Client:** Claude Code 2.1.101 on Windows 11 Pro 10.0.26200
- **Auditor-session ID:** `35b1e783-f26d-4616-acf0-27ad6b14abed` ("Session B" below)
- **Prior attempt session ID:** `908c42e0-cbeb-496b-a28e-df98ca83a425` ("Session A"; triggered by identical prompt 14 minutes earlier, same blocker)
- **Server:** n/a — `server_info` uncallable (roslyn MCP never connected)
- **Catalog version:** n/a (resource `roslyn://server/catalog` unreachable)
- **Experimental surface:** n/a — not loaded
- **Scale:** n/a — not loaded
- **Repo shape:** n/a — not inspected via MCP
- **Plugin installed version:** `roslyn-mcp@roslyn-mcp-marketplace` v1.18.0 at `C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.18.0`
- **Global tool installed:** `darylmcd.roslynmcp` v1.18.0 (`dotnet tool list -g`)
- **Prior issue source:** new blocker — no matching entry in `ai_docs/backlog.md` at HEAD

## 2. Coverage ledger (experimental surface)

All experimental tools/prompts are `blocked` — reason: auditor-session couldn't reach any roslyn MCP tool.

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| * | *all entries from live catalog* | * | **blocked** | 0 | — | Server never connected; live catalog could not be read. See §9.1. |

## 3. Performance baseline
No data — no experimental tool was exercised.

## 4. Schema vs behaviour drift
No data — no tool was exercised.

## 5. Error message quality
No data — no tool error messages were observed.

## 6. Parameter-path coverage
No data — no tool was exercised.

## 7. Prompt verification (Phase 8)
No data — no prompt was exercised.

## 8. Experimental promotion scorecard
All entries: `needs-more-evidence` — reason: *auditor session could not reach the MCP server; no evidence captured this run*.

## 9. MCP server issues (harness / loader)

### 9.1 [HIGH] roslyn plugin MCP does not connect in auditor sessions with cwd `C:\Code-Repo\jellyfin`

**Symptom:** `mcp__roslyn__*` tools not present in the session's tool namespace and not in the deferred-tool list either. Every `roslyn-mcp:*` skill that instructs "call `workspace_load` / `server_info`" fails immediately.

**Reproduced across two independent sessions in the same cwd, same calendar day, fresh Claude Code processes each time:**

| | Session A | Session B (this audit) |
|---|---|---|
| Session ID | `908c42e0-cbeb-496b-a28e-df98ca83a425` | `35b1e783-f26d-4616-acf0-27ad6b14abed` |
| Started (local) | 2026-04-15 ~09:05 | 2026-04-15 ~09:19 |
| Result | roslyn MCP not connected | roslyn MCP not connected |
| `mcp-logs-roslyn/` for cwd cache dir | absent | absent |
| Session-ID appearance in any roslyn log under `%LOCALAPPDATA%\claude-cli-nodejs\Cache\*\mcp-logs-roslyn\*` | zero hits | zero hits |

**What works (same session B):** `python-refactor` (from `~/.claude/mcp.json`), `plugin-github-github` (plugin), `Windows-MCP` (plugin), `ccd_session` + `ccd_directory` (plugin), `scheduled-tasks` (plugin), `mcp-registry` (plugin), `Claude_Preview` (plugin), `Claude_in_Chrome` (plugin). All verified by calling at least one tool on each (e.g. `mcp__scheduled-tasks__list_scheduled_tasks` returned a valid "no tasks" response; `mcp__Windows-MCP__Process` listed processes; `mcp__ccd_directory__request_directory` hit a hook gate which proves connection). **Only `roslyn-mcp` is missing.**

**What the plugin loader is being told:** verified by reading auditor-session's `claude.exe` command line via `Get-CimInstance Win32_Process`:
```
...\claude.exe --output-format stream-json --verbose --input-format stream-json
  --effort max --model claude-opus-4-6[1m] --permission-prompt-tool stdio
  --allowedTools mcp__computer-use,mcp__ccd_session__spawn_task,mcp__ccd_session__mark_chapter
  --setting-sources=user,project,local --permission-mode bypassPermissions
  --allow-dangerously-skip-permissions --include-partial-messages
  --plugin-dir C:\Users\daryl\AppData\Roaming\Claude\local-agent-mode-sessions\skills-plugin\...
  --plugin-dir C:\Users\daryl\.claude\plugins\cache\claude-plugins-official\claude-md-management\1.0.0
  --plugin-dir C:\Users\daryl\.claude\plugins\cache\claude-plugins-official\claude-code-setup\1.0.0
  --plugin-dir C:\Users\daryl\.claude\plugins\cache\claude-plugins-official\github\61c0597779bd
  --plugin-dir C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.18.0
  --replay-user-messages
```
The roslyn `--plugin-dir` **is passed**. The loader should be reading its `.mcp.json` and spawning `roslynmcp`. It is not.

**What the plugin declares:** `C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.18.0\.mcp.json` uses `${user_config.*}` env-var substitution:
```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "roslynmcp",
      "env": {
        "ROSLYNMCP_MAX_WORKSPACES": "${user_config.ROSLYNMCP_MAX_WORKSPACES}",
        "ROSLYNMCP_BUILD_TIMEOUT_SECONDS": "${user_config.ROSLYNMCP_BUILD_TIMEOUT_SECONDS}",
        "ROSLYNMCP_TEST_TIMEOUT_SECONDS": "${user_config.ROSLYNMCP_TEST_TIMEOUT_SECONDS}",
        "ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS": "${user_config.ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS}",
        "ROSLYNMCP_REQUEST_TIMEOUT_SECONDS": "${user_config.ROSLYNMCP_REQUEST_TIMEOUT_SECONDS}"
      }
    }
  }
}
```
No `ROSLYNMCP_*` key exists anywhere in user config (checked `~/.claude/settings.json`, `~/.claude.json`, `~/.claude/mcp.json`, plugin cache). **The template substitutions have no source values.** Possible downstream effects: (a) substitution produces literal `${user_config.X}` strings and env parsing rejects non-numeric values for numeric-typed keys, (b) substitution produces empty string and the server aborts on required-config parse, (c) Claude Code refuses to spawn before calling the server.

**Strongly corroborating evidence — the four repos with working roslyn MCP all have a *local `.mcp.json`* that bypasses the plugin loader:**
```
C:\Code-Repo\DotNet-Firewall-Analyzer\.mcp.json       {command: "roslynmcp"}   no env block
C:\Code-Repo\DotNet-Network-Documentation\.mcp.json   {command: "roslynmcp"}   no env block
C:\Code-Repo\IT-Chat-Bot\.mcp.json                    {command: "roslynmcp"}   no env block
C:\Code-Repo\Roslyn-Backed-MCP\.mcp.json              full env block with ${user_config.*}
```
`C:\Code-Repo\jellyfin` **has no local `.mcp.json`** → relies purely on plugin loading → fails. Three of the four working repos omit the env block entirely; only `Roslyn-Backed-MCP` replicates it (and that's the plugin's *own* repo, where the server may be running from source rather than dotnet-tool).

**Process-tree evidence (important correction to a prior misdiagnosis — see §9.5 below):** 8 `roslynmcp.exe` instances were running during audit. Parent-PID analysis (`Get-CimInstance Win32_Process` with `ParentProcessId`):
```
roslyn PID   Parent PID  Parent process    Start time    RSS
9416         9636        claude.exe        09:01:49 AM   367.9 MB   (session --resume aef3fb58)
26884        20816       claude.exe        09:02:06 AM   1491.2 MB  (session --resume 2bee29e3)
39964        39088       claude.exe        09:02:09 AM   897.9 MB   (session --resume bbc10ec7)
39668        35396       claude.exe        09:02:13 AM   583.8 MB   (session --resume 34ca7601)
29580        38612       Cursor.exe        09:05:48 AM   71.9 MB    (Cursor IDE's own integration)
30516        20824       Cursor.exe        09:05:48 AM   70.9 MB    (ditto)
35436        38612       Cursor.exe        09:05:48 AM   71.2 MB    (ditto)
36600        20824       Cursor.exe        09:05:48 AM   72.5 MB    (ditto)
```
Observations that matter:
- 4 Claude sessions that loaded roslyn all launched via `--resume <UUID>` with `--replay-user-messages`. These are SDK-style / local-agent-mode sessions, not fresh interactive `claude` launches.
- The auditor session's command line is identical **except it has no `--resume` flag** — it's a fresh interactive session. Hypothesis: the plugin-MCP init code path taken for resumed sessions differs from fresh ones, and the fresh path is the broken one. Alternative: resumed sessions cache and reuse a prior-successful MCP server pointer, while fresh sessions must spawn from `.mcp.json` and hit the substitution bug.
- None of the 8 running roslynmcp processes are orphans from the auditor's sessions. Prior diagnosis in Session A (see §9.5) attributed the 4 small ones to "failed handshake orphans"; that was wrong. They're Cursor IDE's own children.

**Reproduction tests the next agent should run (cheapest first):**

**Test #1 — Identify the exact failure mode by dropping a local `.mcp.json` with *no* env block:**
```json
// C:\Code-Repo\jellyfin\.mcp.json
{ "mcpServers": { "roslyn": { "type": "stdio", "command": "roslynmcp" } } }
```
Restart Claude Code in that cwd. Check `mcp__roslyn__server_info`.
- **If tool appears →** plugin-loader's `${user_config.*}` path is broken (or at least: has no fallback when values are unset). Fix candidates: (a) gate the env block on all five values being set, or make them optional with a `default:` field; (b) the plugin's `.mcp.json` template should not use substitution for defaults — inline the defaults literally, or add the values to `userConfig` with `default:` in `plugin.json`; (c) ship the plugin with a sample `.mcp.json` that users can copy to their repo root. Upstream: Claude Code should either (i) resolve `${user_config.X}` to the `userConfig[X].default` in `plugin.json` when no user override exists, or (ii) skip empty substitutions and let the server use its own defaults.
- **If tool still absent →** the plugin-loader isn't spawning `roslynmcp` at all for fresh sessions, independent of env-var values. Escalate to a Claude Code upstream bug; file against `anthropics/claude-code`.

**Test #2 — Confirm it's fresh-session-specific by starting with `--resume`:**
Check whether invoking `claude --resume <existing-session-uuid>` in `C:\Code-Repo\jellyfin` successfully loads the plugin. If yes, confirms the fresh-vs-resumed branch hypothesis.

**Test #3 — Capture plugin-loader diagnostic logs:**
Find the Claude Code main log (`%APPDATA%\Claude\logs\mcp.log`, `main.log`, `main1.log` all exist). `grep` for "roslyn", "plugin", "spawn", "${user_config", "ROSLYNMCP" around the session start timestamp. If the loader logs the rejection reason, the upstream fix is immediate. Auditor did not scan these thoroughly due to scope.

**Owner / next action:** decide between local `.mcp.json` fallback in audited repos (Test #1 fixes auditor-session immediately, lowest-effort) vs. a plugin-side fix adding `default:` to `userConfig` entries (cleanest long-term; requires plugin bump) vs. upstream Claude Code fix (highest leverage; out of this repo's control).

### 9.2 [MEDIUM] `roslyn-mcp:analyze` (and related) skills have no graceful-degradation path when the server isn't connected

**Symptom:** Invoking the skill under the 9.1 conditions loads the skill prompt, which immediately prescribes `workspace_load` and `server_info` — both of which are uncallable. The skill has no "if server not connected, here's what to do" branch. Auditor had to fall back to Bash + manual plugin-cache inspection to discover the issue.

**Evidence:** `C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.18.0\skills\analyze\SKILL.md` (Server discovery section) tells the agent to "call `server_info`" when the tool list is unclear. That tool *requires* the server the skill is meant to prove is working.

**Suggested fix:** Every `roslyn-mcp:*` skill's preamble should include a connection-check step:
1. Check if any `mcp__roslyn__*` tool is in the tool namespace. If not, emit a short standard diagnostic (cwd, plugin-dir presence, cache-dir layout, `.mcp.json` presence) and stop with "Server not connected — see §9.1 of the experimental-promotion backlog."
2. Only proceed to `server_info` once a tool-namespace check confirms connection.

### 9.3 [MEDIUM] Deferred-tool advertisement is a misleading signal for "what's connected"

**Symptom:** Claude Code's "deferred tools" system-reminder lists tools whose schemas are not yet loaded but "available via ToolSearch". This list does *not* reliably indicate which MCP servers are connected — tools from disconnected servers may or may not appear, and tools from connected servers may or may not appear.

**Evidence from session B:** `mcp__scheduled-tasks__list_scheduled_tasks` is not in the initial tool list, not obviously in the deferred list either, yet the tool call succeeds (server is connected). `mcp__roslyn__*` tools are in neither list — and indeed the server isn't connected. But the prior session's Session A ticket draft relied on the deferred list to enumerate what was "broken", which led to false conclusions (§9.5).

**Suggested fix:** upstream — deferred tools should be sourced from *connected but schema-not-loaded* servers only. If a server isn't connected, its tools should not be advertised, or should be marked with a "disconnected: <reason>" field. (Filing this against `anthropics/claude-code` is out of scope for this repo; noted here so the next audit cycle knows.)

### 9.4 [LOW] `mcp-logs-<server>/` cache-dir presence is NOT a reliable connection indicator

**Symptom:** Session A and Session B both have `mcp-logs-plugin-github-github/` and `mcp-logs-python-refactor/` under `%LOCALAPPDATA%\claude-cli-nodejs\Cache\C--Code-Repo-jellyfin\` and nothing else. Session B *also* has working `Windows-MCP`, `scheduled-tasks`, `ccd_session`, `mcp-registry`, `Claude_Preview` tool calls — despite no corresponding `mcp-logs-*/` directories for those servers in the same cwd cache dir. The log dir is clearly created lazily or conditionally (log-level? first-write?), not on every successful handshake.

**Impact:** A diagnostic heuristic used by Session A's ticket draft — "missing `mcp-logs-X/` dir ⇒ X failed to connect" — was **wrong**, which led to §9.5. Documenting here so the next diagnostician doesn't repeat it.

**Suggested fix:** to confirm whether an MCP is connected, *call one of its tools*. Don't look at log-dir presence.

### 9.5 [documented-error] Prior-session ticket draft contained a flawed "whole cohort failed" narrative

**Context (not an upstream bug):** Prior-session (Session A) drafted a support ticket at `C:\Users\daryl\.claude\support-ticket-plugin-mcp-init.md` claiming 5 plugins (`roslyn-mcp`, `Windows-MCP`, `ccd-session`, `Claude_Preview`, `scheduled-tasks`) all failed to load in the affected session. Session B verified — by direct tool calls — that only `roslyn-mcp` is missing; the other 4 all work. The "cohort failure" narrative was built on §9.4's bad heuristic.

Session A also attributed 4 ~70MB `roslynmcp.exe` processes to "failed-handshake orphans" from Claude. Parent-PID analysis in Session B showed they are children of Cursor IDE (§9.1 process table), not Claude at all.

**Cleanup performed by Session B:** ticket draft updated in-place with a correction section, then the whole file replaced with a recall note pointing here (see §12 Cleanup below). Not submitted upstream.

**Lesson for future diagnosis:** `Get-CimInstance Win32_Process -Filter "Name='X.exe'" | Select ProcessId, ParentProcessId, CreationDate` before attributing orphans. Call one tool per server before declaring a cohort failure.

## 10. Improvement suggestions (workflow / UX)

1. **Ship a `.mcp.json` template** in this repo (e.g. `docs/mcp-json-examples/`) that users can copy to any audited repo root. Removes the dependency on plugin-loader `${user_config.*}` behaviour entirely.
2. **Add a connection-check preamble** to every `roslyn-mcp:*` skill (§9.2). Low cost, high debugging-time savings.
3. **Document the plugin-MCP-init failure mode and recovery** in the plugin's README or `ai_docs/backlog.md` — even just "if tools don't appear, drop a local `.mcp.json`" would have saved ~2 hours here.
4. **Surface orphaned `roslynmcp.exe` processes** ≥ N minutes old in a diagnostic skill (`roslyn-mcp:diagnose`?). Both Cursor and Claude appear to leak these; a one-click cleanup would be nice.
5. **Consider a plugin-side bootstrap check**: when `roslynmcp` is spawned and detects all 5 `ROSLYNMCP_*` env vars contain literal `${user_config.X}`, emit a one-line stderr warning and fall back to defaults — rather than failing silently (if that's currently the behaviour).

## 11. Known-issue regression check (Phase 11)
**N/A** — `ai_docs/backlog.md` was not inspected for regressions because no exercise phase ran. Next audit cycle that reaches Phase 11 should cross-reference §9 here.

## 12. Cleanup performed by this audit session
- Prior-session support ticket draft at `C:\Users\daryl\.claude\support-ticket-plugin-mcp-init.md` contained flawed "whole plugin cohort failed" and "orphaned Claude subprocesses" claims (both corrected in §9.5). File was replaced with a short recall-and-pointer note; not submitted upstream.
- No jellyfin repo files were modified by this investigation. Pre-existing modifications visible in `git status` (`.editorconfig`, `Jellyfin.Api/Controllers/UserController.cs`, `Jellyfin.Server.Implementations/Item/BaseItemRepository.cs`, `SharedVersion.cs`, untracked `prompts/`) predate this session.
- No test `.mcp.json` was added to jellyfin. Test #1 in §9.1 is the next agent's call — that's a deliberate, reversible write and should be done with the full `.mcp.json`-vs-plugin-loader experiment in mind.
