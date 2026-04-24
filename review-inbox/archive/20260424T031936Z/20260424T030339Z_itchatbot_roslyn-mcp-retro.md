---
generated_at: 2026-04-24T03:03:39Z
source_repo: itchatbot
source_repo_path: C:\Code-Repo\IT-Chat-Bot
session_phase: mixed
---

# Roslyn MCP session retrospective — itchatbot — 2026-04-24

## 1. Session classification

**mixed — audit-heavy with a single Phase-6 refactor apply.** The bulk of the session was a full-surface MCP audit per `ai_docs/prompts/deep-review-and-refactor.md` against `ITChatBot.sln`: semantic reads, diagnostics, metrics, flow analysis, prompt/resource exercises. A single product-level write was applied (`tests/.editorconfig` → `dotnet_diagnostic.CA1826.severity = suggestion`). No multi-file refactor, no rename cascade, no build/test surgery. Roughly **90% read-side audit / 10% refactor apply**.

## 2. Task inventory

| Task | Tool actually used | File type / domain | Right tool for the job? |
|------|--------------------|---------------------|-------------------------|
| Confirm MCP server ready + catalog-reconcile | `mcp__roslyn__server_info`, `roslyn://server/catalog` | MCP audit | ✅ yes |
| Create isolation branch on main working tree | `Bash(git worktree ..., git checkout -b)` | git | ✅ yes — git's domain |
| Remove stale sibling worktree after allowlist rejection | `Bash(git worktree remove)` | git | ✅ yes |
| Load + warm workspace | `workspace_load`, `workspace_warm` | MCP workspace | ✅ yes |
| Read audit prompt (27k tokens) in chunks | `Read` with offset/limit | markdown | ✅ yes — pre-existing doc, not C# |
| Full-solution diagnostics sweep | `project_diagnostics`, `compile_check` | C# analysis | ✅ yes |
| Security posture | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` | C# analysis | ✅ yes |
| Analyzer inventory + pagination probe | `list_analyzers` | C# analysis | ✅ yes |
| Complexity / cohesion / dead-field scan | `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols`, `find_dead_fields`, `suggest_refactorings` | C# analysis | ✅ yes |
| DI registration inventory | `get_di_registrations` | C# DI | ✅ yes (but truncated — see 2a) |
| Key-type symbol probe (AdapterRegistry) | `symbol_search`, `find_references`, `type_hierarchy`, `find_references_bulk` | C# symbols | ✅ yes |
| Data-flow on ProcessQuestionStreamingAsync | `analyze_data_flow` | C# flow | ✅ yes |
| Snippet / script validation | `analyze_snippet`, `evaluate_csharp` | ephemeral C# | ✅ yes |
| Verify commit/revision for report header | `Bash(git rev-parse HEAD, ls, cat global.json)` | git/fs | ✅ yes |
| Probe format cleanliness | `format_check` | C# formatting | ✅ yes |
| Probe dead-code workflow (refused) | `remove_dead_code_preview` | C# removal | ✅ yes (tool refused — see 2a) |
| Full-solution build | `build_workspace` | dotnet | ✅ yes — Roslyn-backed dotnet invocation |
| Test discovery sample | `test_discover` | test surface | ✅ yes |
| EditorConfig + MSBuild inspection | `get_editorconfig_options`, `evaluate_msbuild_property`, `get_msbuild_properties` | config / MSBuild | ✅ yes |
| Phase-6 write: downgrade CA1826 in `tests/.editorconfig` | `set_diagnostic_severity` | editorconfig | ✅ yes |
| Phase-9 audit-only apply: add pragma + revert | `add_pragma_suppression`, `revert_last_apply` | source edit + undo | ✅ yes |
| Verify post-apply state | `workspace_changes`, host-side `Bash(cat tests/.editorconfig)` | MCP + fs | ✅ yes (but `workspace_changes` incomplete — see 2a) |
| Semantic / reflection / nav samples | `semantic_search`, `find_reflection_usages`, `go_to_definition`, `get_completions` | C# semantic | ✅ yes |
| Prompt rendering probe | `get_prompt_text(discover_capabilities)` | MCP prompts | ✅ yes |
| Negative-probe boundary tests | `find_references(fabricated)`, `revert_last_apply` (2nd call), `analyze_snippet(empty)` | MCP edge | ✅ yes |
| Close workspace | `workspace_close` | MCP lifecycle | ✅ yes |
| Write draft + final audit report | `Write` | markdown | ✅ yes — report is documentation, not C# |
| Read prior audit report for Phase-18 regression | `Bash(head)` | markdown | ✅ yes |

No missed opportunities this session — every Roslyn-shaped task used Roslyn MCP; every non-Roslyn task used the right native tool (git, dotnet build via MCP, markdown edit).

## 2a. Roslyn MCP issues encountered

### `mcp__roslyn__server_info` / `server_heartbeat` — `connection.state=initializing` never advances pre-load
- **Inputs:** fresh stdio host, zero workspaces loaded, probing `server_info` then polling `server_heartbeat`.
- **Symptom:** `"state": "initializing"` on both probes, identical `stdioPid` + `serverStartedAt`. First `server_info` response:

  ```
  "connection": {
    "state": "initializing",
    "loadedWorkspaceCount": 0,
    "stdioPid": 41912,
    "serverStartedAt": "2026-04-24T02:33:22.3410821Z"
  }
  ```

  Re-probe via `server_heartbeat` was identical. State only flipped to `ready` after `workspace_load` succeeded.
- **Impact:** Broke the Phase -1 hard gate (`state==ready` required *before* any workspace load). Cost: one interrupt-and-reconsider cycle with the user before work could proceed.
- **Workaround:** User opted to accept `workspace_load` as part of gate satisfaction.
- **Repro confidence:** deterministic on v1.29.0.

### stdio host recycled mid-session — all workspace + undo state lost
- **Inputs:** long-running audit, ~10 successful tool calls against `workspaceId=8e3f96658f064fcc8fa86cfe38a5ac80`; next call was `diagnostic_details`.
- **Symptom:**

  ```
  Not found: Workspace '8e3f96658f064fcc8fa86cfe38a5ac80' not found or has been closed.
  Active workspace IDs are listed by workspace_list.
  ```

  `server_heartbeat` afterward showed new `stdioPid: 9548`, new `serverStartedAt: 2026-04-24T02:43:51.5057026Z` (10 min drift from original `02:33:22Z`).
- **Impact:** Lost the entire session state — any preview tokens, `workspace_changes` log, `revert_last_apply` undo stack silently invalidated. Cost: ~15 s wasted on the failed calls + 8.5 s re-load + ~30 s of operator re-orientation.
- **Workaround:** `workspace_load` again, switch to the new `workspaceId`, continue.
- **Repro confidence:** intermittent (one recycle observed in this ~30 min session); cause not attributable from inside the client (no log channel).

### `workspace_changes` does not log `set_diagnostic_severity` / editorconfig applies
- **Inputs:** Phase-6 `set_diagnostic_severity(CA1826, suggestion, tests/.editorconfig)` → Phase-9 `add_pragma_suppression` → `revert_last_apply` → `workspace_changes`.
- **Symptom:**

  ```
  "count": 1,
  "changes": [
    { "sequenceNumber": 1, "description": "Apply text edit to SynthesisPromptAssemblerTests.cs",
      "toolName": "apply_text_edit", "appliedAtUtc": "2026-04-24T02:53:15.8996004Z" }
  ]
  ```

  Only the pragma apply (itself reverted) is listed. The earlier `set_diagnostic_severity` write — confirmed on disk via `Bash(cat tests/.editorconfig)` — is absent from the change log.
- **Impact:** A session-undo / audit-summary prompt driven off `workspace_changes` will under-report what was written. Cost: trust-in-log reduced; had to reconcile from disk + memory.
- **Workaround:** Read `tests/.editorconfig` directly to verify the write.
- **Repro confidence:** deterministic.

### `find_dead_fields` ↔ `remove_dead_code_preview` workflow break
- **Inputs:** `find_dead_fields(limit=20)` returned 7 hits including `SysLogServerApiClient._options` with `usageKind=never-read, readReferenceCount=0, writeReferenceCount=1, confidence=high`. Passed that `symbolHandle` straight to `remove_dead_code_preview`.
- **Symptom:**

  ```
  Invalid operation: Symbol '_options' still has references and cannot be removed safely.
  ```

  All 7 `find_dead_fields` hits share the same shape (ctor-written, never-read). None would be removable via the advertised workflow.
- **Impact:** The category's flagship workflow (`find → preview → apply`) cannot remove the very category of dead code `find_dead_fields` was designed to surface. Forced Phase 6 to pivot entirely to editorconfig-level changes.
- **Workaround:** Abandoned dead-code removal; picked a different Phase-6 target.
- **Repro confidence:** deterministic (7/7 hits).

### `diagnostic_details` — 16 s on a single-point probe; parameter naming drift
- **Inputs:** `(file, line=44, column=30, diagnosticId=CA1826)`.
- **Symptom (1 — perf):** `_meta.elapsedMs: 16045` for a point lookup. Principle #3 budget is 5 s.
- **Symptom (2 — drift):** First call used `startLine`/`startColumn` (the sibling convention). Server rejected:

  ```
  The arguments dictionary is missing a value for the required parameter 'line'.
  ```

  Required names are `line` / `column`; every other positional tool uses `startLine` / `startColumn`.
- **Impact:** One round-trip lost to the naming drift; perf is >3× the documented budget and made the call feel stuck.
- **Workaround:** Reload the schema via `ToolSearch`, retry with correct names.
- **Repro confidence:** deterministic on both.

### `find_unused_symbols` — first call failed with parameter-binding JSON error
- **Inputs:** `{workspaceId, includePublic=false, limit=20}` (same shape as other tools in the same batch).
- **Symptom:**

  ```
  Parameter binding failed (JSON deserialization): The JSON value could not be converted
  to System.Boolean. Path: $ | LineNumber: 0 | BytePositionInLine: 7.
  ```

  Path `$` = root. No parameter named. Retry after explicitly loading the schema via `ToolSearch` succeeded on identical inputs.
- **Impact:** One wasted call; error message gave no specific actionable hint beyond "look at the JSON".
- **Workaround:** Force-load schema via `ToolSearch`, retry.
- **Repro confidence:** intermittent; plausibly a client schema-bind race on first dispatch, but the server message was too generic to triage from inside the loop.

### `get_completions` queued 7 s behind stale-reload; returned empty
- **Inputs:** `(AdapterRegistry.cs, line=18, column=16, filterText="To", maxItems=10)` issued right after `set_diagnostic_severity` + `add_pragma_suppression`.
- **Symptom:**

  ```
  "items": [],
  "_meta": { "queuedMs": 7002, "heldMs": 5, "elapsedMs": 7008,
             "staleAction": "auto-reloaded", "staleReloadMs": 7002 }
  ```

  Empty completion list, which may be correct for that caret position but there is no way to tell from the response whether "empty" means "no candidates" or "caret is on a blank line". Also the 7-second queue is entirely from the stale-reload.
- **Impact:** Wall-clock hit; diagnostic ambiguity.
- **Workaround:** Moved on — cross-checked completions behavior from other reads.
- **Repro confidence:** deterministic when a write immediately precedes a read.

### `find_references_bulk` — 13 s including 12.4 s queued behind stale-reload
- **Inputs:** 2-symbol batch (`IAdapterRegistry`, `AdapterRegistry`).
- **Symptom:**

  ```
  "count": 2, "results": [...63 refs...],
  "_meta": { "queuedMs": 12434, "heldMs": 649, "elapsedMs": 13084,
             "staleAction": "auto-reloaded", "staleReloadMs": 12434 }
  ```

  Actual work = 649 ms; the rest was waiting for auto-reload triggered by an unrelated write earlier in the turn. A user looking at the response sees 13 s and concludes the tool is slow.
- **Impact:** Perf-reporting confusion; wall-clock feels wrong.
- **Workaround:** Surface `queuedMs` and `staleReloadMs` in report when present.
- **Repro confidence:** deterministic following writes.

### `build_workspace` diagnostic shape — `endLine` / `endColumn` null
- **Inputs:** full-solution build.
- **Symptom:** two CA1826 entries returned with `"endLine": null, "endColumn": null`, but `project_diagnostics` on the *same* diagnostics populates all four fields.
- **Impact:** Downstream consumers wanting a span have to re-query; agent has to special-case `build_workspace` vs `project_diagnostics`.
- **Workaround:** Cross-reference `project_diagnostics` for complete spans.
- **Repro confidence:** deterministic.

### `get_di_registrations(showLifetimeOverrides=true)` — response exceeded client output cap
- **Inputs:** default scope + non-default `showLifetimeOverrides=true`.
- **Symptom:** response was 64,862 characters — truncated by the client with the standard "Output has been saved to file …" redirection. No server-side pagination parameters on the tool.
- **Impact:** DI audit surface unavailable inline; would have to read it back from the dumped file, which burns context just to parse what the server already knows how to summarize.
- **Workaround:** Skipped the deep DI analysis; documented the tool as "exercised but truncated".
- **Repro confidence:** deterministic on any medium-to-large DI solution.

### `workspace_load` rejected sibling worktree (client-side allowlist)
- **Inputs:** `workspace_load(path=C:\Code-Repo\IT-Chat-Bot-audit-run\ITChatBot.sln, autoRestore=true)`.
- **Symptom:**

  ```
  Parameter '<unknown>' is invalid: Path 'C:\Code-Repo\IT-Chat-Bot-audit-run\ITChatBot.sln'
  is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\IT-Chat-Bot.
  ```
- **Impact:** The audit prompt's "disposable sibling worktree" isolation pattern is structurally unreachable under this client config. Forced a pivot to branch-on-main-tree, which required operator confirmation.
- **Workaround:** Used a named branch `audit-run-20260424` on the main tree.
- **Repro confidence:** deterministic; this is a *client-side* allowlist (not a server defect), but the error message is polished enough to call it out positively.

## 2b. Missing tool gaps

This session hit **no missing-capability gaps in the Roslyn MCP surface itself**. The only genuine capability ask surfaced is a workflow completeness item, already captured in 2a as `find_dead_fields ↔ remove_dead_code_preview`. Everything else — file ops, configuration, project mutation, refactoring primitives, DI audits, prompts — had a fitting tool.

One adjacent gap worth noting, **client-side not server-side**:

### No MCP affordance to expand sanctioned roots mid-session
- **Task:** Add a sibling-worktree directory to the MCP client's sanctioned-root allowlist so a "disposable checkout" audit pattern works.
- **Why not a Roslyn MCP issue:** The allowlist is enforced at the MCP client boundary, before the server ever sees the path. Roslyn MCP's rejection message is correct and actionable.
- **Proposed shape:** client-level — Claude Code should expose `--add-dir` semantics inside a running session (or a `/mcp add-root <path>` command). Server-level is not the right layer.
- **Closest existing tool:** none — this is purely client UX. Flagged here only because it cost this session a real path divergence.

## 3. Recurring friction patterns

### Pattern 1 — post-write stale-reload punishes the *next* read
- **What happened:** After `set_diagnostic_severity` + `add_pragma_suppression`, the next `get_completions` queued 7 s behind auto-reload (`staleReloadMs: 7002`); the next `find_references_bulk` queued 12.4 s (`staleReloadMs: 12434`). `_meta.elapsedMs` reports 13 s of wall-clock that is 96% queue.
- **Why it recurs:** Every Phase-6 apply (or any on-disk write) invalidates the workspace snapshot; the first read after that pays the reload cost, even though the write itself was cheap.
- **What would fix it:** Either (a) make the reload overlap the *write*'s own acknowledgement so the next read isn't penalized, or (b) expose the reload as a separate observable step the caller can await explicitly before the read burst — so perf budgets can be applied to the read alone. Alternatively, surface `staleAction: auto-reloaded` more prominently in the response (it's already there but easy to miss).

### Pattern 2 — host-recycle opacity
- **What happened:** Mid-session, all calls against the first `workspaceId` started returning `NotFound`. `server_heartbeat` revealed a new `stdioPid` and a fresh `serverStartedAt`. No log channel, no notification, no hint about *why* the recycle happened.
- **Why it recurs:** Any long-running audit or refactor session is at risk of losing workspace + undo + `workspace_changes` state to an invisible recycle. Without telemetry the agent can't distinguish "harmless restart" from "OOM / watchdog / crash".
- **What would fix it:** `server_info` should carry `connection.previousStdioPid` + `connection.previousRecycleReason` + `connection.previousExitedAt` on the first probe after a recycle. Even a simple `reason: "idle-eviction" | "watchdog" | "client-disconnect" | "unknown"` enumeration would turn a mystery into a data point.

### Pattern 3 — apply-side tools miss adjacent syntax (this run + prior 2026-04-15 report)
- **What happened:** `remove_dead_code_preview` refuses ctor-written fields — won't co-delete the unused ctor assignment. The prior 2026-04-15 report documented a sibling pattern: `extract_interface_apply` / `extract_type_apply` silently mutated csproj without updating adjacent `<Compile>` items correctly. Quote from 2026-04-15 ledger: *"silently mutated ITChatBot.Configuration.csproj by adding <ItemGroup><Compile Include="IDatabaseOptions.cs" /></ItemGroup>, which then caused MSBuild Duplicate 'Compile' items failure on workspace_reload."*
- **Why it recurs:** Apply-side refactor tools treat the target symbol narrowly (field, type, method body) without owning the surrounding membrane (ctor body that writes it, csproj items that track it, sdk-style implicit includes that overlap). Every time a dead-code or extraction workflow crosses that membrane, it refuses or breaks.
- **What would fix it:** An explicit membrane contract. For `remove_dead_code_preview`: accept `cascadeToWriters=true` to co-remove the setter site AND offer a ctor-param delta preview. For extraction apply tools: route all csproj edits through an SDK-style-aware helper that knows implicit-include behavior. Both share the same fix pattern — make apply tools reason one hop beyond the named symbol.

### Pattern 4 — response-shape drift between sibling tools
- **What happened:** (a) `diagnostic_details` uses `line`/`column`; every other positional tool uses `startLine`/`startColumn`. (b) `build_workspace` diagnostics carry `endLine: null, endColumn: null`; `project_diagnostics` populates them for the same warning. Both are small frictions but both require agents to special-case.
- **Why it recurs:** Any workflow that chains a diagnostic-detection tool into a point-lookup, or reports diagnostics from multiple upstream sources, hits this. It's a one-size-per-tool problem.
- **What would fix it:** A shared `SourceRange` DTO used by every tool that emits locations; accept both naming conventions on input until the sibling convention wins. One pass through the input/output DTO boundary would resolve both concerns.

### Pattern 5 — large responses without server-side pagination
- **What happened:** `get_di_registrations(showLifetimeOverrides=true)` returned 64 KB and was truncated by the client. `list_analyzers` was already paginated; the tool schema explicitly warned about the 1000-test/350 KB `test_discover` danger. The DI tool has no equivalent affordance.
- **Why it recurs:** Medium-to-large solutions (35 projects here) tip response payloads over MCP client caps quickly for any unbounded list. Each uncapped tool is a latent truncation.
- **What would fix it:** Either (a) consistent `offset`/`limit` across every listing tool, or (b) a two-tier default (summary count + `details=true` opt-in, as `workspace_load.verbose` already does well). Prefer (b) — it matches existing idioms.

## 4. Suggested findings

### `dead-fields-removal-workflow-break`
- **Priority hint:** **high** — this breaks the advertised `find_dead_fields → remove_dead_code_preview → remove_dead_code_apply` workflow for every ctor-injected field, which is the most common "dead" shape in modern DI-heavy C#.
- **Title:** Make `remove_dead_code_preview` act on `find_dead_fields` handles or tighten the detector.
- **Summary:** `find_dead_fields` flagged 7 fields with `confidence=high` and `usageKind=never-read`, but `remove_dead_code_preview` refused all of them: *"Symbol '_options' still has references and cannot be removed safely"* (because the ctor still writes to the field). The two tools have incompatible definitions of "dead". This renders the canonical dead-code workflow useless on DI-shaped codebases.
- **Proposed action:** Add `cascadeToWriters=true` on `remove_dead_code_preview` (co-remove the ctor-body assignment; offer a ctor-param delta preview). Alternatively, add `onlyIfAutoRemovable=true` on `find_dead_fields` so it doesn't surface symbols the removal tool won't act on. Prefer the cascade option — agents genuinely want the removal to happen.
- **Evidence:** `2a#find_dead_fields ↔ remove_dead_code_preview`, `3#Pattern 3`.

### `stdio-host-recycle-silent-state-loss`
- **Priority hint:** **high** — silently invalidates preview tokens and undo stacks mid-session with no diagnostic channel.
- **Title:** Surface the host-recycle reason in the next `server_info` / `server_heartbeat` probe.
- **Summary:** Mid-audit, all calls against `workspaceId=8e3f96658f064fcc8fa86cfe38a5ac80` began returning `NotFound`; heartbeat showed a brand-new `stdioPid 9548` with `serverStartedAt` 10 minutes later than the original `serverStartedAt: 2026-04-24T02:33:22.3410821Z`. No recycle reason is exposed anywhere. An agent cannot distinguish eviction-by-cap from a watchdog kill from a crash.
- **Proposed action:** On the first `server_info` / `server_heartbeat` after a recycle, populate `connection.previousStdioPid`, `connection.previousExitedAt`, and `connection.previousRecycleReason` with a small enum (`idle-eviction` / `watchdog` / `client-disconnect` / `unknown`). Even the `unknown` case is a win — it turns an opaque failure into an observed one.
- **Evidence:** `2a#stdio host recycled mid-session`, `3#Pattern 2`.

### `workspace-changes-log-incomplete`
- **Priority hint:** **medium** — the change log silently under-reports editorconfig-level writes.
- **Title:** Include `set_diagnostic_severity` / `set_editorconfig_option` applies in `workspace_changes`.
- **Summary:** After `set_diagnostic_severity(CA1826, suggestion, tests/.editorconfig)` (confirmed on disk), `workspace_changes` reported `count=1` and listed only the later `apply_text_edit` pragma. The editorconfig apply is missing, even though the `set_diagnostic_severity` schema already advertises integration with `revert_last_apply` — so the hook point exists.
- **Proposed action:** Wire the editorconfig-level applies through the same log sink as text edits. Preferably with `affectedFiles: ["tests/.editorconfig"]` and `toolName: "set_diagnostic_severity"` so the existing session-undo prompts don't need to special-case.
- **Evidence:** `2a#workspace_changes does not log set_diagnostic_severity`, `3#Pattern 4`.

### `connection-state-ready-unsatisfiable-pre-load`
- **Priority hint:** **medium** — breaks a structural gate in documented prompts without breaking any actual functionality.
- **Title:** Let `connection.state=ready` mean "transport ready", or document the current semantics.
- **Summary:** `server_info.connection.state` reports `"initializing"` indefinitely on a fresh host with zero loaded workspaces (`stdioPid: 41912, loadedWorkspaceCount: 0, parityOk: true` — everything else indicates health). It only flips to `ready` after the first `workspace_load` succeeds. The `deep-review-and-refactor.md` prompt's Phase -1 hard gate (`Is connection.state == ready?` *before* load) is structurally unsatisfiable on this server.
- **Proposed action:** Option A — rename the pre-load state to `idle` or `ready-no-workspace`, freeing `ready` to mean "transport-reachable". Option B — leave the semantics and update the `server_info` tool summary ("`ready` implies at least one loaded workspace"). Option A is less disruptive to existing prompts.
- **Evidence:** `2a#server_info / server_heartbeat`, Phase -1 gate interaction in transcript.

### `diagnostic-details-perf-over-budget`
- **Priority hint:** **medium** — 3× over the documented single-symbol read budget on a common single-call pattern.
- **Title:** Investigate why `diagnostic_details` costs 16 s for a point probe.
- **Summary:** `diagnostic_details(file, line=44, column=30, diagnosticId=CA1826)` returned in `_meta.elapsedMs: 16045` — more than 3× the 5 s single-symbol-read budget in prompt principle #3. Only one diagnostic was requested. Likely the tool is re-resolving the full project diagnostic set per call rather than accepting a pre-resolved handle.
- **Proposed action:** Accept an optional `fromDiagnostic` locator (file + line + column + id — the exact fields `project_diagnostics` already returns) and short-circuit the project-wide resolution when supplied. If that's architecturally hard, cache the last project-level diagnostic set keyed on `SnapshotToken` and reuse it when unchanged.
- **Evidence:** `2a#diagnostic_details`, `6#perf-baseline` (16045 ms row).

## 5. Meta-note

Session phase was **mixed** (audit-dominant, one small refactor apply). Roslyn MCP friction is currently concentrated in **write-path lifecycle and apply-side completeness** — host recycles invalidate sessions silently (pattern 2), the `workspace_changes` log is under-inclusive (2a), and apply tools stop at the named symbol instead of the surrounding membrane (pattern 3 — a pattern that also shows up in the 2026-04-15 audit against `extract_*_apply`). One thing to change next time: **probe `workspace_list` + `server_heartbeat` between every phase**, not just on an error. It would catch recycles at the boundary instead of mid-call, saving the wasted round-trip and letting the audit record the recycle deterministically instead of inferring it from a downstream `NotFound`.
