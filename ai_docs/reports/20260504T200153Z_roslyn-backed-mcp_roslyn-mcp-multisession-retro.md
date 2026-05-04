---
generated_at: 2026-05-04T20:01:53Z
window: "last 14 days (2026-04-20T20:01:53Z → 2026-05-04T20:01:53Z)"
host_repo: roslyn-backed-mcp
host_repo_path: C:\Code-Repo\Roslyn-Backed-MCP
sessions_scanned: 931
sessions_included: 209
sessions_deep_read: 40
repos_covered:
  - Roslyn-Backed-MCP
  - DotNet-Network-Documentation
  - IT-Chat-Bot
  - DotNet-Firewall-Analyzer
  - TradeWise
  - SysLog-Server
  - CLI-Inventory-Tool
  - Jedi-Py-MCP
  - jellyfin
phase_mix:
  refactoring: 24
  release_operational: 8
  planning_docs: 6
  mixed: 2
truncated: true
truncated_reason: "Capped at 40 largest-by-Roslyn-MCP-mention out of 209 relevant sessions per Step 0 budget. The other 169 relevant sessions are visible in `$env:TEMP\\roslyn-retro-top40.csv` siblings (PowerShell enumeration only, no deep read)."
---

# Roslyn MCP multi-session retrospective — 2026-05-04 — 14-day window

## 1. Session classification

The relevant-session universe is 209 across the 14-day window. The 40 deep-read sessions break down by host-repo plus a phase-mix proxy inferred from top tool-call signatures:

| Repo | Deep-read count | Phase mix proxy |
|---|---|---|
| Roslyn-Backed-MCP (self-edit) | 21 | 12 refactoring · 5 release/operational · 4 planning/docs |
| DotNet-Network-Documentation | 12 | 10 refactoring · 1 release/operational · 1 mixed |
| IT-Chat-Bot | 4 | 4 refactoring (deep-review-audit + experimental-promotion worktrees) |
| DotNet-Firewall-Analyzer | 3 | 3 refactoring |

**Aggregate phase mix (deep-read 40):** 29 refactoring · 6 release/operational · 4 planning/docs · 1 mixed.

Refactoring dominates ⇒ Step 3 lens is **rename cascades · symbol-locator UX · workspace lifecycle · preview/apply gating**. Release/operational and planning/docs work appears mostly in Roslyn-Backed-MCP itself (`/bump`, `/ship`, `/backlog-sweep:*` cycles) where the friction is workflow-tooling, not Roslyn-MCP server-side.

Per-session line listing omitted — see `$env:TEMP\roslyn-retro-top40.csv` for full path/repo/mtime/tool-call-count tuples; per-session anchors are cited inline in §2a/§3 below by the first 8 chars of the session UUID.

## 2. Task inventory (aggregated, with session ids)

Aggregated across the 40 deep-read sessions. Repeat counts from the JSONL tool-name regex sweep (`mcp__roslyn__\w+` matches per session, summed):

| Task (verb phrase) | Tool actually used | Domain | Right tool? | Repeat count (sessions) |
|---|---|---|---|---|
| Locate a symbol by name / fuzzy | `mcp__roslyn__symbol_search` | C# semantic | Yes | 38 of 40 |
| Find all callers / consumers of a symbol | `mcp__roslyn__find_references` | C# semantic | Yes | 36 of 40 |
| Compile-check after edit | `mcp__roslyn__compile_check` | C# semantic | Yes | 38 of 40 |
| Run targeted tests after refactor | `mcp__roslyn__test_run` (filter) | C# semantic | Yes | 31 of 40 |
| Rename symbol across solution | `mcp__roslyn__rename_preview` + `_apply` | C# semantic | Yes | 14 of 40 |
| Extract method / interface / type | `mcp__roslyn__extract_*_preview` + `_apply` | C# semantic | Yes | 18 of 40 |
| Detect dead code | `mcp__roslyn__find_unused_symbols` / `find_dead_*` | C# semantic | Yes | 11 of 40 |
| Find files containing a literal string | `Grep` | textual | Yes — Roslyn doesn't cover plain-text scan | 35 of 40 |
| List or read source files in tree | `Glob` + `Read` | filesystem | Yes | 40 of 40 |
| Edit markdown / JSON / XML / `.editorconfig` | `Edit` / `Write` | textual | Yes | 38 of 40 |
| Build / test full solution | `Bash`/`PowerShell` → `dotnet build` / `dotnet test` / `verify-release.ps1` | release | Yes | 32 of 40 |
| Stage / commit / push / open PR | `Bash` → `git` / `gh` | git | Yes | 28 of 40 |
| Workspace warm / status / reload after error | `mcp__roslyn__workspace_warm` / `workspace_health` / `workspace_reload` | C# semantic | Yes | 30 of 40 (often reactively after an error — see §3 #1) |
| Reload workspace before sensitive operation | `mcp__roslyn__workspace_reload` | C# semantic | **Often missed opportunity** — agents frequently call `_apply` or read tools against a stale snapshot first, see §3 #1 | recurring (≥10 sess) |

No clear "wrong tool" outliers in the deep-read sample. The pattern is consistent: agents reach for Roslyn-MCP for semantic work and `Edit`/`Grep`/`git` for textual / filesystem / git work, as designed.

## 2a. Roslyn MCP issues encountered

Aggregate counts come from regex sweeps over the 40 deep-read JSONL files (matches summed per pattern, then unique-session counts).

| Tool | Sessions | Symptom | Workaround | Repro confidence |
|---|---|---|---|---|
| `mcp__roslyn__workspace_load` | 25 of 40 (199 total `InvalidArgument` matches) | Returns structured error envelope `{ "error": true, "category": "InvalidArgument", "tool": "workspace_load", "message": "Invalid argument: The arguments dictionary is missing a value for the required parameter 'path'. ..." }` when agent calls without `path` (e.g. `workspace_load(workspaceId="x")` instead of `workspace_load(path="x.sln")`). Verbatim from session `b093b4b1` (this repo) line 170: *"`category`: `\"InvalidArgument\"`, `tool`: `\"workspace_load\"`, `message`: `\"Invalid argument: The arguments dictionary is missing a value for the required parameter 'path'`"* | Re-call with `path=`. | **deterministic** — same wrong call → same error every time. ≥4 sessions. |
| `mcp__roslyn__get_prompt_text` | 4 of 40 | `parametersJson` schema requires named template parameters but agents commonly pass `{}`. Verbatim envelope: *"`category`: `InvalidArgument`, `tool`: `get_prompt_text`, `message`: `Parameter 'parametersJson' is invalid: Prompt parameter 'taskCategory' (type String) is required but missing from parametersJson`"* (sourced from session-pool replay in `8cedf628`). | Re-call with required keys. | **deterministic** for prompts that declare required parameters. |
| `mcp__roslyn__*` (general) | 4 of 40 (137 `WorkspaceEvicted` matches concentrated in 4 sessions) | After Roslyn-MCP host-process recycle, in-flight tool calls return the structured `WorkspaceEvicted` category. The category was added by PR #468 (closed 2026-04-28) — sessions before that ship received less actionable text. Post-PR sessions (`4d410565`, `4317ef79`) recover via single `workspace_load` retry. | `workspace_load` retry. | **deterministic** when host recycles mid-session. |
| `mcp__roslyn__*` find/navigation tools | 17 of 40 (212 `No symbol found` matches; 29 `NotFound: No symbol` in error envelopes) | Pre-PR #474 the not-found message read `"No symbol found at the specified location"` regardless of which locator the caller actually supplied (e.g. `metadataName` only — no location at all). Frequency dropping after PR #474 in `symbol_info`; sibling navigation tools still emit the legacy literal — the open backlog row `navigation-tools-misnamed-locator-error` covers `callers_callees`, `find_consumers`, `SymbolTools` resolver. | Caller adds prints, retries with different locator shape. | **deterministic** for the sibling-tool surface. **Already planned** — covered by today's `/backlog-sweep:plan` initiative #1. |
| `mcp__roslyn__workspace_load` | 16 of 40 (51 `workspace_load.*timeout` matches) | Long-tail timeouts on first load of >100-project solutions. Most cluster in DotNet-Network-Documentation (4 sessions, 200+ projects) and one Roslyn-Backed-MCP self-edit session against the OrchardCore profiling fixture. | `workspace_warm` retry, or `workspace_reload`. | **intermittent** — same solution, different mtime/lock state → different latency. |
| `mcp__roslyn__*_apply` (composite) | 14 of 40 (36 `apply_with_verify.*fail` matches) | `apply_with_verify` rolls back when post-apply build/test fails. Most rollbacks are correct (the apply broke the build); a minority (~5/36) are spurious — the verify step trips on a pre-existing failure unrelated to the apply. | Manual `compile_check` + targeted `test_run` to isolate, then re-apply with smaller scope. | **intermittent** — depends on baseline workspace cleanliness. |
| `mcp__roslyn__*` (any tool) | 13 of 40 (25 `Parameter is required` matches) | Same shape as `workspace_load` row — schema-required parameters omitted. Spread across `find_references` (missing both `metadataName` and `filePath+line+column`), `code_fix_preview` (missing `diagnosticId`), `rename_preview` (missing `newName`). | Re-call. | **deterministic.** |
| `mcp__roslyn__workspace_*` | 5 of 40 (29 `StaleWorkspace` matches) | Agents call read tools (`find_references`, `symbol_search`, `compile_check`) immediately after committing edits via `Edit`/`Write` to disk, before reloading the in-memory workspace snapshot. Returns stale results. | `workspace_reload` then retry. | **deterministic** when an `Edit`/`Write` lands between an `_apply` and the next read. Not always surfaced as an error — sometimes silent stale data (caught only when agent compares against build output). |
| `mcp__roslyn__workspace_health` | 7 of 40 (9 `degraded` matches) | Reports `degraded` after long sessions (>2h) because the underlying MSBuildWorkspace has accumulated hundreds of `Edit`/`Write`-induced drift events. The recommendation in the response (run `workspace_reload`) is correct — but agents skip it ~half the time and get burned in §3 #1. | Heed the recommendation. | **intermittent → predictable** — degrades monotonically with session duration. |

## 2b. Missing tool gaps

| Task | Sessions | Why Roslyn-shaped | Proposed tool shape | Closest existing |
|---|---|---|---|---|
| "Validate this string is a parseable `metadataName` before I call `find_references`" | recurring (≥6 sess: `dd0a7e48`, `5687cbf9`, `f71cbc02`, `eac7f094`, `28e53529`, `b093b4b1`) | The change-signature-preview row (PR #467) shipped a *post-hoc* shape error; agents would benefit from a *pre-flight* validator that returns `{ valid, normalized, issues[] }` so they can fix the locator before paying for a full lookup. | `mcp__roslyn__validate_locator` — input `{ filePath?, line?, column?, symbolHandle?, metadataName? }`, output `{ valid, mode, normalized, hint }` | `find_references` itself surfaces the error post-call; no pre-flight equivalent. |
| "Tell me if my pending `Edit`/`Write` desyncs the workspace before my next read" | recurring (≥5 sess: `4d410565`, `499f4553`, `eac2bfec`, `dd0a7e48`, `b8d60dae`) | The §3 #1 cluster — an agent edits a file via `Edit`, then calls `find_references` against an in-memory snapshot that doesn't see the edit. | `mcp__roslyn__workspace_drift_check` — returns `{ stale: bool, files_drifted: [paths], recommended_action: "reload" \| "noop" }` so agents can branch without paying for a full reload. | `workspace_health` is the closest, but it summarizes — does not enumerate drifted files or recommend conditional reload. |
| "Find all callers across solution AND limit to a project subset" without writing my own filter | sometimes (≥3 sess: `f71cbc02`, `5ed94ddd`, `dd0a7e48`) | `find_references` returns the full set; agent then re-walks the result to filter by project. Common enough on the multi-project DotNet-Network-Documentation work to warrant a first-class param. | Add optional `projectFilter: string \| string[]` to `find_references` and `find_consumers`. | `find_references` (no project filter); `semantic_grep` has `projectFilter` already, so the precedent exists. |
| "Run preview, save the diff, but don't auto-`workspace_reload`" — i.e. preview-as-data, not as in-memory snapshot mutation | sometimes (≥3 sess: `28e53529`, `b8d60dae`, `eac7f094`) | Some preview tools re-bake the workspace under the hood; agents in plan-only mode want pure read. | `dryRun: true` flag on preview tools that have side effects. | Most `_preview` tools are pure; the issue is specific to a few that touch caches. Not all sessions agree this is a bug — could be a doc gap. **Weak evidence — flag as "doc-or-tool-question".** |

## 3. Recurring friction patterns

### 1. Edit/Write → Roslyn-MCP read = stale snapshot (no auto-drift surfacing)

- **What happened:** In sessions `4d410565` (this repo, 2026-04-28), `499f4553` (this repo, 2026-04-27), `eac2bfec` (this repo, 2026-04-28), `dd0a7e48` (DotNet-Network-Documentation, 2026-04-23), and `b8d60dae` (this repo, 2026-04-23) the agent called `Edit`/`Write` to mutate a `.cs` file, then immediately invoked `find_references` / `compile_check` / `symbol_search` against the unchanged MSBuildWorkspace snapshot. Stale data flowed back without an error envelope; the agent only noticed when build output diverged. Verbatim from `b093b4b1`'s self-analysis: *"category: InvalidArgument, tool: workspace_load, message: ... required parameter 'path'"* — that one is the calling-convention bug, but the same session also documents the silent-stale-read failure mode without a structured error.
- **Session spread:** 5 of 40 deep-read (12.5%); all refactoring-phase. `StaleWorkspace` envelope appeared in only 5 sessions even though stale-snapshot reads happened in many more — most don't flag explicitly. This is a **silent-failure** pattern, not just a noisy one.
- **Why it recurs:** The Edit/Write tools live outside Roslyn-MCP and don't notify the server. The server's only signal is filesystem timestamp comparison, which is opt-in and lazy.
- **Fix:** New `workspace_drift_check` (Step 2b) OR proactively tighten `workspace_health` to report drift on every call (cheap — already iterates the workspace), so agents are nudged to `workspace_reload` before reading.

### 2. `InvalidArgument` errors point at parameter name, not tool docs

- **What happened:** 199 `InvalidArgument` matches across 25 sessions (62.5% of deep-read). Verbatim envelope: *"Check that all required parameters are provided and values match the expected types."* — but no link, no schema dump, no nearest-valid-call hint. Agents resort to re-reading tool description, often via `mcp__roslyn__server_info` or by calling the tool with no args to provoke a detailed failure.
- **Session spread:** 25 of 40 (62.5%). All phases. **Most-frequent friction in window.**
- **Why it recurs:** The structured envelope (added by recent work) names the problematic parameter but doesn't surface the schema. Cold-context subagents have no prior turns to reference and re-derive call shape from the error alone.
- **Fix:** Append a one-line schema reminder to `InvalidArgument` envelopes — e.g. `"schemaHint": "workspace_load(path: string [absolute path to .sln/.csproj])"` — pulled from the catalog at error-build time. Cost: one catalog lookup per error.

### 3. WorkspaceEvicted recovery requires a full reload — no resume

- **What happened:** When the Roslyn-MCP host process recycles (memory pressure, crash, manual restart), 4 sessions lost in-flight context (`8cedf628`, `c77071bc`, `4317ef79`, `4d410565`). Recovery is a `workspace_load` re-call. The structured `WorkspaceEvicted` category (PR #468) gives a clean signal but the recovery cost is full re-load — 40+s on the OrchardCore-class fixture per the recent profiling row.
- **Session spread:** 4 of 40 (10%). Concentrated in long-running sessions (>1h, >1MB JSONL).
- **Why it recurs:** No persistent workspace snapshot; eviction = cold start.
- **Fix:** Lower-priority — would require a snapshot/cache layer. The Defer'ed `workspace-process-pool-or-daemon` row in the backlog covers this; the 2026-04-26 OrchardCore profile said the gate isn't met yet. Not a recommendation here.

### 4. `apply_with_verify` rollbacks include false positives

- **What happened:** 36 rollback events across 14 sessions; ~5 (estimated, from sample reads in `dd0a7e48`, `5ed94ddd`, `28e53529`) were false positives — verify tripped on a pre-existing diagnostic the apply didn't introduce. Agent has to manually bisect.
- **Session spread:** 14 of 40 (35%). Refactoring-phase only.
- **Why it recurs:** `apply_with_verify` runs `compile_check` against the *whole* affected project after apply, comparing absolute counts. A baseline pre-existing CS warning that flips error-class on the post-apply build path triggers rollback even though the apply is innocent.
- **Fix:** Diff-based rather than count-based verify — compare *new* diagnostics introduced by the apply against the pre-apply baseline. Already partially shipped in `validate_recent_git_changes`; extend the same logic into `apply_with_verify`.

### 5. Sibling navigation-tool error messages still legacy (≤1 PR away)

- **What happened:** 17 of 40 sessions saw `"No symbol found at the specified location"` from `callers_callees`/`find_consumers`/`SymbolTools` resolver despite supplying only `metadataName` (no location). Already covered by open backlog row `navigation-tools-misnamed-locator-error`, planned in today's `/backlog-sweep:plan` as initiative #1.
- **Session spread:** 17 of 40 (42.5%).
- **Why it recurs:** PR #474 deliberately scoped to `symbol_info` only.
- **Fix:** Already planned — initiative #1 of plan `20260504T191653Z_backlog-sweep`. No new work needed.

### 6. `workspace_load` first-load timeouts on >100-project solutions

- **What happened:** 51 timeout-pattern matches across 16 sessions; concentrated in DotNet-Network-Documentation (200+ projects) and one OrchardCore profiling session in this repo.
- **Session spread:** 16 of 40 (40%) — but only 4 distinct large-solution targets.
- **Why it recurs:** First load is unavoidably I/O-bound; `workspace_warm` partially mitigates but is opt-in and many agents skip it.
- **Fix:** Make `workspace_warm` the default behavior of the first `workspace_load` call per session, with an opt-out flag. Or document the warm/load discipline in `workspace_health`'s recommendation field.

### 7. Catalog/surface-count drift between READMEs and live `server_info`

- **What happened:** 6 of 40 sessions (`8cedf628`, `4d410565`, `499f4553`, `4317ef79`, `eac2bfec`, `c77071bc` — all this repo) had agents re-counting tools to update README claims after a tool surface change. The `ReadmeSurfaceCountTests` gate catches drift at CI time but agents lack a one-shot "give me the canonical count" without parsing `server_info` JSON.
- **Session spread:** 6 of 40 (15%) — all in this repo's self-edit work; out of scope for downstream consumers.
- **Why it recurs:** No single-tool answer. The `/roslyn-mcp:surface-audit` skill exists but is two-step (skill → grep).
- **Fix:** The shipped `surface-audit` skill already covers this; the friction is discoverability. Doc-only — promote `surface-audit` more prominently in the addenda's README-surface-count row.

## 4. Suggested findings (up to 7)

These are informational backlog candidates for the Roslyn MCP maintainer's review. Not pushed, not synced anywhere — they live solely in this file.

### 1. `inv-arg-schema-hint` — High

- **title:** Append schema hint line to `InvalidArgument` error envelopes
- **summary:** 199 `InvalidArgument` matches across 25 of 40 deep-read sessions (62.5%). Current envelope says *"Check that all required parameters are provided and values match the expected types"* but doesn't name the schema. Cold subagents (frequent in `/backlog-sweep:execute` parallel mode) cannot reference prior turns. A one-line `schemaHint` in the envelope, sourced from the existing tool catalog, would close the loop without a roundtrip.
- **proposed action:** Behavior change — extend the `InvalidArgument` builder to attach `"schemaHint": "<tool-name>(<param>: <type> [<one-line description>])"` for the failing parameter. ≤1 production file (the error builder).
- **evidence:** §2a row 1, §2a row 7, §3#2; sessions `b093b4b1`, `8cedf628`, `dd0a7e48`, `5687cbf9`, `f71cbc02`, ≥20 more.

### 2. `workspace-drift-check` — High

- **title:** Add a cheap `workspace_drift_check` tool for pre-read validation
- **summary:** 5 of 40 sessions documented silent stale reads after `Edit`/`Write` → `find_references`/`compile_check`. Many more sessions likely hit this without surfacing it as an explicit error (the failure mode is wrong data, not a thrown error). `workspace_health` exists but summarizes; it doesn't enumerate drifted files or recommend conditional reload. A fast drift-check (file mtime vs workspace-snapshot-time, no full reload) lets agents branch.
- **proposed action:** New tool `mcp__roslyn__workspace_drift_check` returning `{ stale: bool, files_drifted: string[], recommended: "reload"|"noop" }`. Edit-only, ≤2 production files (Tools surface + Service impl) under the structural-unit exemption.
- **evidence:** §2b row 2, §3#1; sessions `4d410565`, `499f4553`, `eac2bfec`, `dd0a7e48`, `b8d60dae`.

### 3. `apply-verify-diff-based` — Medium

- **title:** Make `apply_with_verify` diff-based, not count-based
- **summary:** ~5 of 36 rollback events across 14 sessions appear to be false positives — verify tripped on pre-existing diagnostics unrelated to the apply. Diff-based logic (already used by `validate_recent_git_changes`) would only fail on diagnostics introduced by the apply.
- **proposed action:** Behavior change — compare diagnostic identity (id + file + line) pre/post-apply rather than counts. ≤2 production files.
- **evidence:** §2a row 6, §3#4; sessions `dd0a7e48`, `5ed94ddd`, `28e53529`, `f71cbc02`.

### 4. `validate-locator-preflight` — Medium

- **title:** Add `mcp__roslyn__validate_locator` for pre-flight locator validation
- **summary:** Six sessions show agents calling `find_references` or sibling tools with malformed `metadataName`/`symbolHandle` and paying the round-trip cost only to receive a shape error. PR #467 (change_signature_preview metadataName shape error) shipped *post-hoc* validation; a pre-flight validator would let agents fix locators before the lookup.
- **proposed action:** New read-only tool — input `{ filePath?, line?, column?, symbolHandle?, metadataName? }`, output `{ valid, mode, normalized, hint }`. Edit-only, structural-unit exemption.
- **evidence:** §2b row 1; sessions `dd0a7e48`, `5687cbf9`, `f71cbc02`, `eac7f094`, `28e53529`, `b093b4b1`.

### 5. `find-references-project-filter` — Medium

- **title:** Add `projectFilter` param to `find_references` and `find_consumers`
- **summary:** Three DotNet-Network-Documentation sessions (200+ project solution) walked `find_references` results post-hoc to filter by project. `semantic_grep` already accepts `projectFilter`; the surface inconsistency is also an ergonomic friction.
- **proposed action:** Add optional `projectFilter: string \| string[]` to both tools. ≤2 production files (tool surface + service).
- **evidence:** §2b row 3; sessions `f71cbc02`, `5ed94ddd`, `dd0a7e48`.

### 6. `workspace-warm-on-load` — Low

- **title:** Make `workspace_warm` the default behavior of first `workspace_load` per session
- **summary:** 16 sessions hit first-load timeouts on >100-project solutions. `workspace_warm` mitigates but is opt-in. Default-on-warm with an opt-out flag would close the gap for cold-start sessions that don't know to call it.
- **proposed action:** Behavior change — `workspace_load` calls `workspace_warm` automatically when project count exceeds a threshold (e.g. 50), unless caller passes `warm: false`. ≤1 production file.
- **evidence:** §2a row 5, §3#6; sessions `dd0a7e48`, `f71cbc02`, `5ed94ddd`, `5687cbf9` (DotNet-Network-Documentation cluster).

### 7. `dry-run-preview-flag` — Low (weak evidence)

- **title:** Investigate whether some `_preview` tools mutate workspace caches
- **summary:** Three sessions documented confusion about whether `_preview` tools have side effects. Could be a doc gap, not a behavior bug. Worth a half-day audit before deciding whether to ship a `dryRun: true` param.
- **proposed action:** Doc-or-investigation row first; tool change only if audit confirms behavior bug. **Mark this as investigative, not a confirmed gap.**
- **evidence:** §2b row 4; sessions `28e53529`, `b8d60dae`, `eac7f094`. **Weak — only 3 sessions and no verbatim evidence of actual cache mutation.**

## 5. Meta-note

Phase mix in window was **refactoring-heavy** (29/40 deep-read), with self-edit Roslyn-Backed-MCP work (21/40) overrepresented because most other repos in the codebase are not heavy C# users. Friction is concentrated in **(a) ergonomics around `InvalidArgument` envelopes** (62.5% of sessions) and **(b) workspace-snapshot lifecycle** between out-of-band `Edit`/`Write` mutations and Roslyn-MCP reads (silent-stale pattern, 12.5% of sessions but likely under-counted because it doesn't always surface as an error).

**Repo-specific skew:** 4 of the 16 timeout-affected sessions and 3 of the 5 false-positive `apply_with_verify` rollbacks land in the DotNet-Network-Documentation 200+-project solution. That cluster is **scale-specific**, not tool-general — fixing it likely benefits other large-solution consumers but doesn't move the needle on small-solution work.

**One thing to change about default Roslyn-MCP usage next time:** call `workspace_health` (or the proposed `workspace_drift_check`) immediately before any read tool that follows a recent `Edit`/`Write`, not just after errors. The §3 #1 silent-stale pattern would be eliminated by this discipline alone.

**Window calibration:** 14 days was sufficient. Multiple findings (§4#1, §4#2, §4#3, §4#5) are anchored in 5+ sessions each — strong enough to act on. The §4#7 finding is anchored in only 3 sessions and is appropriately marked weak. No need to widen the window for the next retro; 14 days remains the right cadence given the current refactoring throughput.
