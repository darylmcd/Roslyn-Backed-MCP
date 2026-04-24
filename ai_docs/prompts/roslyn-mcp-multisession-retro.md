# Multi-session retrospective — Roslyn MCP issues, gaps, and recommendations (cross-repo, last N days)

Review **recent Claude Code sessions across all repos** and produce a structured retrospective **report file** that captures Roslyn MCP server issues, missing-tool gaps, and recommendations worth fixing. The report is saved **locally** in the Roslyn MCP repo as a self-contained artifact — it is not pushed, appended, or synced to any external backlog. The maintainer can read the file directly when they want to triage findings.

Follow the steps in order — do not skip step 0 or step 1.

---

## Step 0 — Pick the window and discover sessions (do this first)

Claude Code persists session transcripts as JSONL files under `~/.claude/projects/<encoded-repo-path>/*.jsonl` (one directory per repo, one `.jsonl` per session). **You are looking across all of them, not just the current session.**

1. **Window** — default to **the last 14 days** measured by file mtime. If the user named a different window in their invocation (e.g. "last 7 days", "last month"), honor that. State the chosen window explicitly in your first user-facing line.
2. **Enumerate session files** — for each subdirectory of `~/.claude/projects/`, list `*.jsonl` files whose mtime falls inside the window. Record for each: `(repo_slug, session_file_path, session_start_ts, session_end_ts, approx_line_count)`. The repo slug is recoverable from the directory name (Claude Code encodes the repo path with `-` separators — decode it back to a repo root when possible; fall back to the encoded form if ambiguous).
3. **Filter to relevant sessions** — a session is relevant if **any** of:
   - It contains at least one `mcp__roslyn__*` tool call, OR
   - It contains a `/roslyn-mcp:*` skill invocation, OR
   - It contains a verbatim reference to `roslyn-mcp`, `Roslyn MCP`, or a Roslyn MCP tool name in text content.

   Sessions with zero Roslyn-MCP surface area are dropped silently — they will not appear in §2 but will be counted in the meta-note so the reader knows the sample size.
4. **Budget reads** — if the total JSONL payload in window exceeds ~200k lines, cap at the 40 largest-by-Roslyn-MCP-mention sessions and note the truncation in §5. Do not load all files into context blindly; use `Grep` / `jq` / `rg` over the JSONL to extract only tool-call and error records before deep-reading.

### Resolve the report path

1. **Repo name** — the Roslyn MCP repo itself: basename of the current working directory (the repo this prompt was launched in), slugified to `[a-z0-9-]`. The report lands **here**, not in each source repo.
2. **Timestamp** — UTC, compact ISO-8601: `YYYYMMDDTHHMMSSZ`. Use `date -u +%Y%m%dT%H%M%SZ`.
3. **Path** — `ai_docs/reports/<timestamp>_<repo-slug>_roslyn-mcp-multisession-retro.md` relative to the current repo root. Create `ai_docs/reports/` if it does not exist.
4. **Echo the resolved path and the window** in your first user-facing line so the user can redirect before work begins.

Assemble the full content, then `Write` once at the end of Step 4.

---

## Step 1 — Classify each included session (one line per session)

For every relevant session found in Step 0, state which phase dominated:

- **refactoring** — C#/code edits, symbol-level changes, semantic moves
- **release/operational** — version bumps, ship+merge, CI plumbing, git ops
- **planning/docs** — markdown/backlog/plan.md edits, no code mutation
- **mixed** — explicitly call out the split

Emit a compact table: `| session_id (short) | repo | date | phase | notes |`. This classification **determines the lens for step 3** on a per-session basis. Do not apply a refactoring-tool analysis to a session that did no code work.

Also compute the **aggregate mix** (e.g. "8 sessions: 5 refactoring / 2 release-operational / 1 mixed") — that aggregate is what step 3 hangs off.

---

## Step 2 — Enumerate file-modifying and workflow tasks (aggregated across sessions)

For every task across the included sessions that touched a file OR ran a non-trivial command, record:

| Session | Task (one-line verb phrase) | Tool actually used | File type / domain | Right tool for the job? |

If a task type repeats across sessions (e.g. "rename symbol across projects" happens in 4 sessions), collapse into one row with a repeat count rather than listing it 4 times — but keep the session-id list in the row so evidence is traceable.

"Right tool" criterion is unchanged:
- Roslyn MCP for C# semantic work (references, symbols, refactors, diagnostics)
- `Edit`/`Write` for markdown/JSON/XML or small textual edits
- `git` / `gh` / `dotnet` for their native domains
- Mark **missed opportunity** ONLY when the Roslyn surface genuinely covered the task and was bypassed.

---

## Step 2a — Roslyn MCP issues encountered across sessions (required)

For **every** Roslyn MCP tool invocation across the included sessions that errored, returned wrong or partial results, timed out, was flaky, had confusing output, or required a retry — record one row:

- **Tool** — exact Roslyn MCP tool name (e.g. `mcp__roslyn__symbol_search`)
- **Sessions** — list of session ids where this failure mode appeared (drives `Repro confidence`)
- **Inputs** — redacted/summarized inputs that triggered it
- **Symptom** — what went wrong (error text, wrong result, perf, UX)
- **Impact** — which task was blocked/slowed in each session, rough cumulative time cost
- **Workaround** — fallback used (Grep, Edit, manual, another Roslyn tool)
- **Repro confidence** — one-shot (1 session) / intermittent (2–3 sessions, same error) / deterministic (≥4 sessions OR same input → same failure every time)

Quote the verbatim error or output from the JSONL — include the session id next to the quote so it is traceable. No hypotheticals.

Collapse identical failures across sessions into one row (don't list the same stack trace 6 times); distinct failure modes of the same tool get separate rows.

If there were zero issues in window, say so explicitly — a clean run across N sessions is a data point, not an excuse to skip the section.

---

## Step 2b — Missing Roslyn MCP tool gaps (required)

For every task across the included sessions where **no Roslyn MCP tool fit but one semantically should have** (forcing fallback to Grep/Edit/raw `dotnet`), record:

- **Task** — what was being attempted
- **Sessions** — which sessions hit this gap (repeat count matters — a gap seen once is weaker evidence than one seen five times)
- **Why Roslyn-shaped** — C#/semantic reasoning that argues for first-class coverage
- **Proposed tool shape** — name, one-line description, input/output sketch
- **Closest existing tool** — if any, and why it fell short

If Step 2's "right tool" column flagged a missed opportunity and the root cause was a capability gap (not user error), it belongs here. Cross-session recurrence is a strong signal — flag any gap seen in ≥3 sessions as **recurring** in the row.

---

## Step 3 — Recurring friction patterns (cross-session)

List up to **7** patterns (raised from 5 because the window is wider) where Roslyn MCP friction appeared in **≥2 sessions**, OR where a single-session failure pattern cost material time AND looks structurally likely to recur. For each:

- **What happened** (1-2 sentences, tied to verbatim quotes from specific sessions — cite session ids — no hypotheticals)
- **Session spread** — how many of the N included sessions hit this, and which phases they were in
- **Why it recurs** (e.g. "every rename across projects", "every DI audit on a large solution")
- **What would fix it** (one concrete proposal — new tool, behavior change, better error message, doc change)

Adapt the lens to the dominant phase mix from step 1:

- refactoring-heavy mix → symbol_search precision, rename cascades, preview/apply gaps, test-fixup
- release/operational-heavy mix → version-bump behavior, nuget-preflight coverage, workspace-health signals
- planning/docs-heavy mix → likely out of scope for Roslyn MCP — note briefly and move on

---

## Step 4 — Assemble the report and write it

Compose the full report body and `Write` it to the path resolved in Step 0. This local file is the only deliverable. Use this file structure (fields shown as angle-bracket placeholders — substitute real values):

Frontmatter (YAML, between `---` lines):

- `generated_at`: ISO-8601 UTC timestamp
- `window`: "last N days (<start_ts> → <end_ts>)"
- `host_repo`: repo-slug of the Roslyn MCP repo where this report lives
- `host_repo_path`: absolute path
- `sessions_scanned`: total sessions in window across all repos
- `sessions_included`: subset that touched Roslyn MCP
- `repos_covered`: list of repo slugs
- `phase_mix`: object with counts for `refactoring`, `release_operational`, `planning_docs`, `mixed`
- `truncated`: boolean; if true, note which sessions were dropped and why

Body sections, in order:

- `# Roslyn MCP multi-session retrospective — <human date> — <N>-day window`
- `## 1. Session classification` — Step 1 per-session table + aggregate mix line
- `## 2. Task inventory (aggregated, with session ids)` — Step 2 table
- `## 2a. Roslyn MCP issues encountered` — Step 2a rows, verbatim quotes preserved, session ids attached
- `## 2b. Missing tool gaps` — Step 2b rows, recurrence counts attached
- `## 3. Recurring friction patterns` — Step 3 list, up to 7
- `## 4. Suggested findings (up to 7)` — ranked backlog-candidate findings for the Roslyn MCP maintainer's review. **Do not push, append, or sync these anywhere** — this list is informational only and lives solely in this file. For each finding:
  - **id** — short kebab-case slug (e.g. `refactor-timeout-on-large-sln`)
  - **priority hint** — low / medium / high, with a one-line justification grounded in cross-session recurrence
  - **title** — imperative verb phrase (≤ 80 chars)
  - **summary** — 2-4 sentences tying the finding to concrete Step 2a/2b/3 evidence (quote verbatim where possible, cite session ids)
  - **proposed action** — new tool / behavior change / docs / error-message fix
  - **evidence** — explicit cross-reference: `2a#<tool-name>`, `2b#<task>`, or `3#<pattern>`, plus session-id list

  Quality beats quantity — skip anything you can't pin to a session quote or concrete step. A finding that only shows up in one session should be clearly marked as such (it may still be important, but the priority hint should reflect weaker evidence).
- `## 5. Meta-note` — Step 5 output

---

## Step 5 — Meta-note (3-5 sentences, lives in §5 of the report)

Cover:

1. The phase mix of the window (e.g. "5 refactoring / 2 release / 1 mixed").
2. Where Roslyn MCP friction is currently concentrated across the window (coverage / reliability / ergonomics / docs).
3. Any repo-specific skew worth flagging (e.g. "3 of 5 refactor failures were on repo X's 200-project solution — may be scale-specific, not tool-general").
4. One thing you would change about default Roslyn MCP usage next time.
5. Whether the window was long enough — if most findings came from 1–2 sessions, recommend widening the window on the next retro.

This calibrates future retros and surfaces tool-level learning, not just project-level gaps.

---

## Step 6 — Confirm and stop

After writing the file:

1. Print the resolved report path.
2. Print a one-line summary: window, sessions scanned, sessions included, repos covered, aggregate phase mix, count of Step 2a issues, count of Step 2b gaps, count of §4 findings.
3. STOP. Do not commit, do not branch, do not PR, do not touch any external backlog. The user reads the file and decides what (if anything) to do with it.
