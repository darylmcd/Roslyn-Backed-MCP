# Multi-session retrospective — Roslyn MCP issues, gaps, and recommendations (Claude Code + Codex, cross-repo, last N days)
<!-- purpose: Prompt for producing a multi-session Roslyn MCP retrospective report from Claude Code and Codex transcripts. -->

Review recent agent sessions from **both** Claude Code and Codex across all repos and write one local report file in this repo capturing Roslyn MCP server issues, missing-tool gaps, and recommendations. The report is the only deliverable: no commit, no PR, no backlog write.

Both harnesses are in scope. Each reaches the server through different plumbing (tool-name prefixing, lazy tool-search, context budget, retry policy), so a single-harness read misattributes harness bugs as server bugs. If either source is unreadable or empty, say so in §0 and §5 and set `sources_degraded: true`.

---

## 0. Window, sources, filter

**Window** — default last 14 days by file mtime; honor a window the user named. State the window, the resolved report path, and per-source session counts in your first user-facing line.

**Sources**

| | Claude Code | Codex |
|---|---|---|
| Files | `~/.claude/projects/<encoded-repo-path>/*.jsonl` — one dir per repo; worktrees get their own dir (attribute to the parent repo, keep the worktree marker in notes) | `~/.codex/sessions/<YYYY>/<MM>/<DD>/rollout-*.jsonl` (live) **and** `~/.codex/archived_sessions/rollout-*.jsonl` (flat, no date dirs). Scan both — the archived dir has carried all Codex evidence in past windows |
| Session id | `.jsonl` basename (UUID) | `session_meta.payload.id` |
| Repo | decode the dir name against real `C:/Code-Repo/*` names — a blanket `-`→`/` replace mangles hyphenated repos | `session_meta.payload.cwd` (authoritative; the file path carries only a date) |
| Tool call | `.message.content[]` entry with `.type=="tool_use"`; `.name` is flat and fully qualified | `response_item` with `.payload.type=="function_call"`; `.payload.name` is the **bare** tool name and `.payload.namespace` carries the server (`mcp__roslyn`). No flat `mcp__roslyn__<tool>` string exists in Codex records |
| Tool result | `tool_result` matched by `tool_use_id`; `is_error` or error text | `function_call_output` matched by `call_id`; error text or non-success status |
| Subagents | separate session files | separate rollouts with `thread_source: "subagent"` and `parent_thread_id` — roll up under the parent and count them in frontmatter |
| Other useful records | `custom-title`, `last-prompt` | `token_count` (context pressure), `turn_context`, `tool_search_output` (schema dumps — never evidence of a call) |

`~/.codex/session_index.jsonl` gives thread titles. `~/.codex/logs_2.sqlite` is multi-GB and not a source. Extract call, result, and error records with `rg`/`jq` first; deep-read only sessions that carry findings.

**Tool-name matching is prefix-agnostic.** The client-assigned prefix follows the registration key: `mcp__roslyn__symbol_search` on the dev-build entry, `mcp__plugin_roslyn-mcp_roslyn__symbol_search` on the marketplace plugin, other prefixes on other keys. Claude: match `mcp__\S*roslyn\S*__\S+`. Codex: match a `namespace` containing `roslyn`. A false-positive server is visible at read time; a dropped prefix silently loses whole repos (three-quarters of Claude-side calls in a recent window sat under the plugin prefix). Normalize every tool name to flat `mcp__roslyn__<tool>` in the report so cross-harness collapse works.

**Relevance filter** — include a session if any of: at least one actual Roslyn MCP invocation per the rule above; a `/roslyn-mcp:*` skill invocation; substantive discussion of Roslyn MCP or a named tool in user/assistant text. Never a bare full-file grep for `roslyn` — it matches every Codex rollout (schema dumps, `cwd`, AGENTS.md preambles) and half the archived hits carry no real call. Dropped sessions are counted per source in §0 and §5, not listed.

**Budget** — no fixed session cap. Record extraction is cheap, and the old 40-session cap dropped 81 of 111 relevant Codex sessions in one window. If you must truncate, keep per-source balance, list the dropped sessions and why in §0, and set `truncated: true`.

**Report path** — `ai_docs/reports/<UTC YYYYMMDDTHHMMSSZ>_<cwd-basename slugified [a-z0-9-]>_roslyn-mcp-multisession-retro.md` in this repo (create `ai_docs/reports/` if absent). Write once, at the end.

---

## 0a. Normalize

One record per Roslyn MCP invocation from either source: `agent` (`claude` | `codex`), `tool` (flat normalized), `call_id`, `inputs` (Codex `arguments` is a JSON string — parse it), `result`, `errored`, `ts`, `repo`, `session_id`, `is_subagent`, `parent_session_id`. `agent` is a required column on every downstream table row. Note harness facts that affect interpretation: Codex `model_provider` / `cli_version`, Claude model, context-window sizes, and whether tool schemas were lazily loaded via tool-search.

---

## 1. Classify sessions

One row per included session: `| agent | session_id (short) | repo | date | phase | subagent? | notes |`. Phase is one of refactoring / release-operational / planning-docs / mixed (name the split). Compute the aggregate mix and the per-harness mix; a lopsided split is itself a finding about which harness the report characterizes.

---

## 2. Task inventory

For every task that touched a file or ran a non-trivial command: `| Agent(s) | Session(s) | Task | Tool actually used | File type / domain | Right tool? |`. Collapse repeats into one row with a count and per-agent breakdown (`codex ×3, claude ×1`). Right tool: Roslyn MCP for C# semantic work; `Edit`/`Write` for markdown/JSON/XML/small text; `git`/`gh`/`dotnet` for their domains. Mark **missed opportunity** only when the Roslyn surface covered the task and was bypassed. For Codex, check `tool_search_output` first: a tool the harness never surfaced is a **discoverability** finding, not model judgment — say which.

---

## 2a. Roslyn MCP issues (required)

One row per distinct failure mode (errored, wrong or partial result, timeout, flaky, confusing output, needed retry), collapsed across sessions and harnesses:

| Tool | Agents (`claude` / `codex` / `both`) | Sessions (harness-tagged) | Inputs (summarized) | Symptom (verbatim quote; one per harness when `both`) | Impact | Workaround | Repro confidence | Attribution |

- Repro confidence: one-shot (1 session) / intermittent (2–3) / deterministic (≥4, or same input → same failure). Reproduction in both harnesses upgrades one level.
- Attribution: `server-side` (both harnesses, or error text unambiguously from the server) / `harness-specific` (one harness, and the failure implicates client plumbing — marshalling, namespace, timeout, truncation) / `unknown`. Fill it on every row; it is the point of reading both sources.

Zero issues is a data point — say so, and say if one harness was clean while the other was not.

---

## 2b. Missing tool gaps (required)

One row per task where no Roslyn MCP tool fit but one semantically should have (fallback to Grep / Edit / raw `dotnet`):

| Task | Agents / Sessions | Why Roslyn-shaped | Proposed tool shape (name, one-liner, in/out) | Closest existing tool + why it fell short | Real gap or discoverability gap? |

Flag ≥3 sessions as **recurring** and both harnesses as **cross-harness**. Step 2 missed opportunities whose root cause is a capability gap belong here.

---

## 3. Recurring friction patterns

Up to 8 patterns seen in ≥2 sessions, or a single costly pattern that is structurally likely to recur. Each: what happened (quote-backed), session spread with the Claude/Codex split and phases, harness sensitivity (both, or client-specific — and what that implies about root cause), why it recurs, one concrete fix. Pick the lens from the Step 1 mix: refactoring → symbol precision, rename cascades, preview/apply, test-fixup; release → version-bump, nuget-preflight, workspace health; planning/docs → mostly out of scope, note briefly. Reserve one slot for a **cross-harness parity** pattern (same operation, different behavior per client); if none, say so.

---

## 4. Write the report

Frontmatter (YAML):

| key | value |
|---|---|
| `generated_at` | ISO-8601 UTC |
| `window` | `"last N days (<start> → <end>)"` |
| `host_repo`, `host_repo_path` | slug and absolute path of this repo |
| `sources_scanned` | e.g. `["claude-code", "codex"]` |
| `sources_degraded`, `sources_degraded_reason` | bool; reason string or null |
| `sessions_scanned`, `sessions_included` | `{claude, codex, total}` |
| `codex_subagent_sessions_rolled_up` | int |
| `repos_covered` | list of slugs |
| `phase_mix`, `phase_mix_by_agent` | `{refactoring, release_operational, planning_docs, mixed}`; the latter keyed by agent |
| `issues_by_attribution` | `{server_side, harness_specific, unknown}` |
| `truncated` | bool; if true, which sessions, which source, why |

Body, in order:

- `# Roslyn MCP multi-session retrospective — <date> — <N>-day window — Claude Code + Codex`
- `## 0. Sources and coverage` — per-source paths, found / included / dropped counts, degradation, subagent roll-up. A reader must see at a glance that both harnesses were read.
- `## 1. Session classification` — Step 1 table, aggregate mix, per-harness mix
- `## 2. Task inventory (aggregated, with agent + session ids)`
- `## 2a. Roslyn MCP issues encountered`
- `## 2b. Missing tool gaps`
- `## 3. Recurring friction patterns`
- `## 4. Suggested findings (up to 8)` — ranked, informational only. Each: **id** (kebab slug), **priority hint** (low / medium / high plus a one-line justification from cross-session and cross-harness recurrence), **title** (imperative, ≤80 chars), **summary** (2–4 sentences, quote-backed), **proposed action**, **surface** (`server` / `claude-harness` / `codex-harness` / `docs`, derived from the 2a attribution — a client-side fix is not a server defect), **evidence** (`2a#<tool>`, `2b#<task>`, `3#<pattern>` plus agent-tagged session ids). Skip anything not pinned to a quote; mark single-session or single-harness findings as such.
- `## 5. Meta-note`

Evidence rule for every section: quote verbatim from the JSONL; tag each quote with agent, session id, and a locator (the `call_id` for a tool call, the record line number or message id for prose) so a reader can find it without a text search; no hypotheticals.

---

## 5. Meta-note

Brief. Cover: phase mix per harness; sample balance and whether it biases the findings (one harness under 20% of included sessions means the report mostly characterizes the other); where friction concentrates (coverage / reliability / ergonomics / docs) and whether that differs by harness; the server-side vs harness-specific split and where fix effort should go; repo-specific skew (scale-specific vs tool-general); one default-usage change per harness; whether the window was long enough (findings resting on 1–2 sessions or one harness → widen or rebalance next time).

---

## 6. Confirm and stop

Print the report path and one summary line (window; sessions scanned and included per source; repos; phase mix; 2a issues by attribution; 2b gaps; §4 findings). If a source was degraded, print a second line naming it. Then stop: no commit, branch, PR, or backlog write.
