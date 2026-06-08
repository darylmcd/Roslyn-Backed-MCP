---
generated_at: 2026-06-08T20:30:50Z
window: "last 14 days (2026-05-25 → 2026-06-08)"
host_repo: roslyn-backed-mcp
host_repo_path: C:/Code-Repo/Roslyn-Backed-MCP
sessions_scanned: 270
sessions_included: 9
repos_covered: [roslyn-backed-mcp, dotnet-firewall-analyzer]
repos_scanned: [roslyn-backed-mcp, dotnet-network-documentation, tradewise, users-daryl-claude, bioremote, syslog-server, biofiletransfer, dev-sync, it-chat-bot, windows-system32, jedi-py-mcp, syslog-server-mcp, dotnet-firewall-analyzer]
phase_mix:
  refactoring: 2
  release_operational: 6
  planning_docs: 1
  mixed: 0
truncated: false
---

# Roslyn MCP multi-session retrospective — 2026-06-08 — 14-day window

> **⚠️ CORRECTION (2026-06-08, post-publication — Directive #5/#7).** The "near-zero cross-repo dogfooding" headline (§3#cross-repo, §5) was a **measurement error** and is **superseded by this block.** The genuine-usage scan matched only the bare tool prefix `mcp__roslyn__` (the dev-build registration used inside this repo) and **missed the marketplace-plugin namespace `mcp__plugin_roslyn-mcp_roslyn__`** that consumer repos use. Re-derived authoritative in-window counts (JSON `tool_use` records only — raw string counts inflate via tools-catalog dumps):
>
> | repo | real roslyn `tool_use` | sessions | note |
> |---|---|---|---|
> | Roslyn-Backed-MCP | 89 | 8 | dev build (`mcp__roslyn__`) |
> | **BioRemote** | **19** | **3** | **MISSED originally** — incl. a clean 7× `rename_preview`→`rename_apply` cascade on `SecretServerRestClient.DSS.*` NSwag DTOs + 2× `evaluate_csharp` |
> | DotNet-Firewall-Analyzer | 1 | 1 | `server_info` probe |
> | TradeWise / BioFileTransfer / DotNet-Network-Documentation | 0 | 0 | no real calls in-window (TradeWise window was frontend-heavy: playwright+postgres) |
>
> **Corrected sessions_included = 12 (not 9).** Corrected conclusion: Roslyn **is** used cross-repo and works for its flagship semantic feature (rename cascade) when invoked; availability is not the blocker (it ships as a global plugin). The real gap is **frequency/reach (steering/discoverability), not non-use and not missing capability.** §2a (test_run, preview-token, etc.) is unaffected — those came from correctly-counted Roslyn-Backed-MCP sessions. Backlog row retitled `roslyn-mcp-cross-repo-steering-gap` reflects the corrected framing.

> **Method note (read first — it changes how the numbers below are read).** A naive scan for the string `mcp__roslyn__` matched **269 of 270** in-window sessions. That count is **noise**: the agent definitions in the system prompt (`handoff-prep`, `plan-deepener`, `plan-remediator`, `workspace-health-triage`, etc.) and the deferred-tool reminders echo every `mcp__roslyn__*` tool name into every transcript, plus `CLAUDE.md`/`AGENTS.md` name the server. The flat ~43–44 baseline showed up even in Rust (`SysLog-Server`), Python (`Jedi-Py-MCP`), and `Windows-system32` sessions — repos that cannot call the server. **The honest signal is actual `tool_use` invocations** (`"name":"mcp__roslyn__…"`). By that measure only **9 sessions** genuinely touched the Roslyn MCP surface (8 in this repo + 1 in `DotNet-Firewall-Analyzer`), totalling ~90 tool calls. Everything below uses the 9-session genuine set; the 261 dropped sessions are accounted for in §5.

## 1. Session classification

| session (short) | repo | date | phase | notes |
|---|---|---|---|---|
| `800f50fc` | roslyn-backed-mcp | 2026-06-01 | release/operational (QA) | `/mcp-server-surface-test` — 62 roslyn calls; **most "errors" are deliberate negative-path probes** the server correctly rejected. 3 sample-app `.cs` edits. |
| `b70ec703` | roslyn-backed-mcp | 2026-05-27 | **refactoring** | `/top-5-remediation` on `WorkspaceManager.cs`; genuine dogfooding: `document_symbols`→`complexity_metrics`→`symbol_search`→`find_references`→`compile_check`→`test_run`. 7 `.cs` edits. |
| `8fa58da6` | roslyn-backed-mcp | 2026-05-30 | **refactoring** | `/top-5-remediation`; **10 `.cs` edits via `Edit`/`Grep`**, roslyn used only for a `server_info` readiness probe (under-use). |
| `326102a1` | roslyn-backed-mcp | 2026-05-30 | release/operational | `/release-cut`; roslyn = `server_info` readiness probe only. |
| `9c721289` | roslyn-backed-mcp | 2026-06-01 | release/operational | `/release-cut`; `server_info` only; 12 `.cs`/version-file edits via `Edit`. |
| `52279e38` | roslyn-backed-mcp | 2026-06-08 | release/operational | `/ship`; `server_info` only. |
| `e932d2aa` | roslyn-backed-mcp | 2026-06-08 | release/operational | "are both layer 1 and layer 2 up to date?" (update check); `server_info` only. |
| `86d182c9` | roslyn-backed-mcp | 2026-05-28 | planning/docs | `/brainstorm` (opportunity-scan); `server_info` only — out of roslyn scope. |
| `f9c003f4` | dotnet-firewall-analyzer | 2026-05-27 | release/operational | `upgrade-eligibility-matrix`; `server_info` only — NuGet upgrade analysis done via `Bash`/`dotnet`. |

**Aggregate mix:** 9 sessions = **2 refactoring / 6 release-operational / 1 planning-docs / 0 mixed.** Only **2** sessions (`800f50fc` synthetic surface-test, `b70ec703` real refactor) did substantive semantic work; the other 7 touched roslyn only via a one-shot `server_info` readiness probe.

## 2. Task inventory (aggregated, with session ids)

| Session(s) | Task (verb phrase) | Tool actually used | Domain | Right tool? |
|---|---|---|---|---|
| `800f50fc` | Audit the live Roslyn MCP server surface against a loaded C# repo | `mcp__roslyn__*` ×62 (incl. intentional probes), `Bash`/`Read`/`Write` | MCP server QA | ✅ roslyn is the subject |
| `b70ec703` | Navigate + validate a refactor of `WorkspaceManager.cs` | `document_symbols`, `get_complexity_metrics`, `symbol_search`, `find_references`, `workspace_reload`, `compile_check`, `test_run` | C# semantic | ✅ correct |
| `8fa58da6` | Remediate C# (10 `.cs` edits) under `/top-5-remediation` | `Edit`/`Grep`/`Bash`; roslyn = `server_info` only | C# semantic | ⚠️ **missed opportunity** — `find_references`/`rename_apply`/`compile_check` available, used `Edit`/`Grep` |
| `326102a1`, `9c721289` | Run release-cut pipeline (bump→verify→ship→tag) | `Bash`/`git`/`dotnet`/`Edit`; roslyn = `server_info` readiness | release/git | ✅ readiness probe appropriate |
| `52279e38` | Ship pipeline (commit→push→PR→merge) | `Bash`/`git`/`gh`; roslyn = `server_info` | release/git | ✅ correct |
| `e932d2aa` | Verify Layer-1/Layer-2 version currency | `server_info` + `Bash` | release/ops | ✅ correct |
| `86d182c9` | Opportunity-scan / brainstorm (markdown) | `Read`/`Edit`; roslyn = `server_info` | planning/docs | ✅ out of roslyn scope |
| `f9c003f4` | Build NuGet upgrade-eligibility matrix | `Bash`/`dotnet`/`Edit`; roslyn = `server_info` | .NET deps | ⚠️ **soft missed opp** — `get_nuget_dependencies`/`nuget_vulnerability_scan` exist (see §2b) |

**Repeat-collapsed pattern:** "roslyn interaction was a `server_info` readiness probe **only**" appears in **7 of 9** included sessions (`8fa58da6`, `326102a1`, `9c721289`, `52279e38`, `e932d2aa`, `86d182c9`, `f9c003f4`).

## 2a. Roslyn MCP issues encountered

> **Calibration:** the bulk of `800f50fc`'s ~25 error results are **intentional negative-path probes** from `/mcp-server-surface-test` (bad JSON, missing required params, out-of-range lines, `startLine>endLine`, nonexistent workspace/symbol handles, self-referencing project, empty query). In every one the server returned a **well-structured** error with `category`, `exceptionType`, `schemaHint`, and `_meta`. Those are a **positive** signal (the validation layer works) and are **not** counted as issues below. Only genuine friction is rowed.

### 2a#test_run — opaque, unstructured failure on a compound filter — *strongest finding*
- **Tool:** `mcp__roslyn__test_run`
- **Sessions:** `b70ec703`
- **Inputs:** `filter` = compound `|`-OR of 4 clauses: `FullyQualifiedName~WorkspaceSessionLoaderFailureTests|FullyQualifiedName~WorkspaceManagerEvictionTests|FullyQualifiedName~AutoReloadCascadeHostCrashTests|FullyQualifiedName~WorkspaceLoadRestoreRaceTests`
- **Symptom (verbatim, `b70ec703` L401):** `An error occurred invoking 'test_run'.` — **no `category`, no `schemaHint`, no `_meta`**, unlike every other error in the corpus. The failure mode is invisible to the caller.
- **Impact:** blocked the batched 4-class run; recovered by re-issuing **4 separate single-filter calls** (`b70ec703` L403/409/410/412 — all succeeded).
- **Workaround:** split the compound filter into N single-clause `test_run` calls.
- **Repro confidence:** one-shot (1 session) — **but structurally deterministic** for compound/`|`-joined filters; high-confidence as a defect because it violates the structured-error convention the rest of the surface follows.

### 2a#PreviewTokenStale — apply token invalidated by workspace reload
- **Tool:** `create_file_apply`, `delete_file_apply` (apply-family)
- **Sessions:** `800f50fc`
- **Inputs:** `*_apply` with a preview token issued **before** an intervening `workspace_reload`.
- **Symptom (verbatim, `800f50fc` L334/337/351):** `Preview token '…' has expired: the workspace was reloaded after the preview was created, invalidating the stored solution snapshot. Re-issue the paired *_preview call.`
- **Impact:** minor here — partly deliberate (one call used a literal `PLACEHOLDER` token). Working-as-designed.
- **Workaround:** re-issue the `*_preview` then `*_apply` without a reload in between.
- **Repro confidence:** deterministic **by design** — every reload invalidates outstanding tokens. Matches the standing memory `[[project_apply_token_stale_on_autoreload]]` ("never batch preview+apply").

### 2a#server_info-firstcall — first roslyn call missing while server still connecting
- **Tool:** `mcp__roslyn__server_info` (the session's first roslyn call)
- **Sessions:** `800f50fc`
- **Symptom (verbatim, `800f50fc` L18):** `<tool_use_error>Error: No such tool available: mcp__roslyn__server_info</tool_use_error>` — then **succeeded on retry** (L28).
- **Impact:** trivial (one retry).
- **Workaround:** retry after deferred MCP tools load (or route through `workspace-health-triage`).
- **Repro confidence:** one-shot. **Root cause is the Claude Code deferred-MCP-tool bootstrap** (server reported "still connecting" at session start), not a server defect — but it shapes first-call roslyn ergonomics.

### 2a#preview-params — required-param / param-name friction on preview/mutation tools
- **Tools:** `create_file_preview` (needs `projectName`), `move_type_to_file_preview` (needs `sourceFilePath`, not `filePath`), `move_file_preview` (needs `targetFilePath`), `dependency_inversion_preview` (needs `typeName`/`interfaceProjectName`)
- **Sessions:** `800f50fc`
- **Symptom:** repeated `InvalidArgument` — e.g. `create_file_preview` failed 4× near-identically (L330/350/352/353/354) on missing `projectName`; `move_type_to_file_preview` (L322/325) passed `filePath` where the schema wants `sourceFilePath`. Each carried a **correct `schemaHint`** (e.g. `move_type_to_file_preview(workspaceId, sourceFilePath, typeName, targetFilePath?)`).
- **Impact:** first-call failures, all recoverable via `schemaHint`. In this session largely **intentional** surface-probing; the *ergonomic* observation (inconsistent `filePath`↔`sourceFilePath` naming; `projectName` required even when the path implies the project) is the takeaway.
- **Workaround:** read `schemaHint`, resupply.
- **Repro confidence:** intermittent within 1 session; an ergonomics smell, not a reliability bug.

**Cross-repo issues:** none recorded — the C#-heavy consumer repos never invoked roslyn (see §3#cross-repo), so they generated zero tool errors. A clean run is *not* the story here; **near-zero usage** is.

## 2b. Missing tool gaps

### 2b#nuget-upgrade-matrix — per-project upgrade-eligibility matrix
- **Task:** produce a per-project NuGet/TFM upgrade-eligibility matrix (current TFM, candidate TFMs, per-package latest-compatible version, transitive blockers) — `f9c003f4`'s `upgrade-eligibility-matrix` run.
- **Sessions:** `f9c003f4` (1 — **weak evidence**).
- **Why roslyn-shaped:** needs the project graph + MSBuild TFM resolution + NuGet dependency closure, all of which the server already models internally.
- **Proposed tool shape:** `nuget_upgrade_matrix(workspaceId)` → `{ projects: [{ name, currentTFM, candidateTFMs[], packages: [{ id, current, latestCompatible, blockedBy[] }] }] }`.
- **Closest existing tool:** `get_nuget_dependencies` + `nuget_vulnerability_scan` + the `nuget-preflight` skill cover *pieces*; none emits a single eligibility matrix. The session used raw `dotnet`/`Bash` instead.

> **Honest framing of §2b:** the window produced **one** genuine, weakly-evidenced capability gap. The far stronger "gap-shaped" signal is **non-use of existing, capable tools** (C# work routed through `Edit`/`Grep` when `find_references`/`rename_apply`/`compile_check` exist). That is a discoverability / default-usage problem, not absent capability — captured in §3#cross-repo and §3#server_info-only rather than invented as a fake tool gap here.

## 3. Recurring friction patterns

### 3#cross-repo — near-zero cross-repo dogfooding *(headline)*
- **What happened:** the C#-heavy consumer repos engaged `.cs` source heavily but **never** called roslyn-mcp. Verified aggregate (in-window, real `tool_use` count = 0 for all): **BioRemote 47/63 sessions touched `.cs`, TradeWise 31/68, BioFileTransfer 19/31, DotNet-Network-Documentation 11/26** — **0 roslyn calls across all of them.**
- **Session spread:** 0 of 9 included sessions came from a consumer repo; ~108 consumer-repo sessions touched C# with no roslyn call. Phases: mixed refactoring/operational.
- **Why it recurs:** roslyn-mcp is not wired into those repos' skills/`AGENTS.md`; `Edit`/`Grep`/`dotnet` are the defaults. The server is effectively exercised only against its own synthetic `SampleLib` (via the surface-test) and its own source.
- **What would fix it:** decide explicitly — either (a) wire roslyn into BioRemote/TradeWise/BioFileTransfer C# workflows (workspace-load preflight + prefer semantic tools), or (b) document that roslyn-mcp's intended scope is self-hosting only. Today it is ambiguous and the server gets almost no real-world exercise.
- *Caveat:* inferred from aggregate signal (touched `.cs` + 0 roslyn calls); not per-task verified that roslyn would have helped each specific edit.

### 3#server_info-only — roslyn used as a readiness oracle, not for semantic work
- **What happened:** in **7 of 9** included sessions the only roslyn call was `server_info` (or `workspace_list`). Even a refactoring session, `8fa58da6`, did **10 `.cs` edits** with roslyn touched only for a readiness probe.
- **Session spread:** `8fa58da6` (refactoring) + 6 operational/planning sessions.
- **Why it recurs:** `workspace-health-triage` and skill preflights probe `server_info` on entry; remediation/release flows then default to `Edit`/`Bash`. The semantic tools are never reached.
- **What would fix it:** in refactor/remediation skills, *after* the readiness probe, load the workspace and route C# symbol edits through `find_references`/`rename_apply`/`compile_check` instead of `Edit`/`Grep`.

### 3#test_run-opaque-error — unstructured failure breaks the error convention
- **What happened:** `b70ec703` L401 — `test_run` with a compound `|` filter returned the bare string `An error occurred invoking 'test_run'.`; recovered by splitting into 4 single-filter calls.
- **Session spread:** 1 session, but structurally recurs for any compound/multi-clause filter.
- **Why it recurs:** the compound-filter path appears to escape the structured-error wrapper used everywhere else.
- **What would fix it:** wrap `test_run` failures in the standard envelope (`category`, `message`, `schemaHint`, `_meta`); if compound `|` filters are unsupported, say so explicitly.

### 3#preview-token-stale — apply tokens die on reload
- **What happened:** `800f50fc` — `*_apply` after `workspace_reload` → `PreviewTokenStale` (L334/337/351).
- **Session spread:** 1 session; deterministic by design.
- **Why it recurs:** every reload (incl. auto-reload) invalidates outstanding preview tokens; `revert_last_apply` is single-slot.
- **What would fix it:** already mitigated by the standing "never batch preview+apply" discipline (`[[project_apply_token_stale_on_autoreload]]`); the residual ask is a doc/error-message reminder, which the message already provides well.

### 3#preview-param-friction — inconsistent param names trip first calls
- **What happened:** `800f50fc` — `move_type_to_file_preview` wanted `sourceFilePath` but was given `filePath`; `create_file_preview` required `projectName`; etc. — clusters of `InvalidArgument`, all recoverable via `schemaHint`.
- **Session spread:** 1 session (largely intentional probing).
- **Why it recurs:** `filePath` vs `sourceFilePath` naming differs across mutation tools; required params (`projectName`) duplicate information already implied by the path.
- **What would fix it:** standardize param naming across the mutation/preview family; infer `projectName` from `filePath` when the workspace makes it unambiguous.

## 4. Suggested findings (up to 7)

> Informational only. **Not pushed, appended, or synced anywhere** — this list lives solely in this file for the maintainer to triage.

### `dogfood-roslyn-cross-repo`
- **priority hint:** **high** — strongest cross-session evidence in the window (~108 consumer-repo sessions touched `.cs`, 0 roslyn calls); without real-world use, regressions surface only in the synthetic surface-test.
- **title:** Wire roslyn-mcp into BioRemote/TradeWise/BioFileTransfer C# workflows, or document self-hosting-only scope
- **summary:** Across 14 days, BioRemote (47/63 sessions touching `.cs`), TradeWise (31/68), and BioFileTransfer (19/31) invoked roslyn **zero** times — C# work went through `Edit`/`Grep`/`dotnet`. The server is exercised almost exclusively against its own `SampleLib` and source. Either adopt it in those repos (workspace-load preflight + prefer semantic tools) or state explicitly that its scope is self-hosting.
- **proposed action:** behavior/process — add a roslyn workspace-load + semantic-tool nudge to those repos' `AGENTS.md`/skills; **or** a one-line scope statement in this repo's docs.
- **evidence:** `3#cross-repo`, `§5`; sessions: all consumer-repo sessions (none in the included 9).

### `test-run-structured-error`
- **priority hint:** **medium-high** — a concrete, self-contained defect with a clear fix; capped below high only because it was seen in **one** session (`b70ec703`).
- **title:** Return a structured error envelope from `test_run` instead of the bare "An error occurred invoking 'test_run'"
- **summary:** `b70ec703` L401: a compound `|`-OR `test_run` filter produced `An error occurred invoking 'test_run'.` with no `category`/`schemaHint`/`_meta` — invisible to the caller — forcing a fallback to 4 separate single-filter calls (L403/409/410/412). Every other error in the corpus is well-structured; this one is the lone exception.
- **proposed action:** error-message/behavior fix — wrap `test_run` failures in the standard envelope; if compound filters are unsupported, reject with an explicit `schemaHint`.
- **evidence:** `2a#test_run`, `3#test_run-opaque-error`; sessions: `b70ec703`.

### `route-csharp-edits-through-roslyn`
- **priority hint:** **medium** — recurs across `8fa58da6` + the cross-repo pattern; addresses *under-use* of shipped capability.
- **title:** In refactor/remediation skills, route C# symbol edits through roslyn after the readiness probe
- **summary:** 7/9 included sessions touched roslyn only via `server_info`; `8fa58da6` did 10 `.cs` edits via `Edit`/`Grep` despite `find_references`/`rename_apply`/`compile_check` being available. The readiness probe fires but the semantic tools are never reached.
- **proposed action:** behavior — skills like `/top-5-remediation` should `workspace_load` and prefer roslyn semantic ops for C# symbol work.
- **evidence:** `3#server_info-only`, `2#task-inventory`; sessions: `8fa58da6`, `b70ec703` (counter-example: done right).

### `preview-param-consistency`
- **priority hint:** **medium** — ergonomics; low blast radius but a repeatable first-call stumble.
- **title:** Standardize mutation-tool param names and infer `projectName` from path
- **summary:** `800f50fc` showed `move_type_to_file_preview` wanting `sourceFilePath` (given `filePath`) and `create_file_preview` requiring `projectName` (failed 4×). The `schemaHint` recovers it, but inconsistent naming guarantees repeated first-call `InvalidArgument`s for any caller without the schema in front of it.
- **proposed action:** API ergonomics — align `filePath`/`sourceFilePath` across the family; infer `projectName` from `filePath` when unambiguous.
- **evidence:** `2a#preview-params`, `3#preview-param-friction`; sessions: `800f50fc`.

### `deferred-tool-first-call-doc`
- **priority hint:** **low-medium** — root cause is the harness, not the server; one-shot, trivial impact.
- **title:** Document the "first roslyn call may miss while the server is still connecting" startup behavior
- **summary:** `800f50fc` L18: `Error: No such tool available: mcp__roslyn__server_info`, then fine on retry — the deferred-MCP-tool bootstrap, not a server bug. A doc line ("call `workspace-health-triage` / expect one retry on the first roslyn call of a session") would set expectations.
- **proposed action:** docs — note in the server/skill onboarding docs.
- **evidence:** `2a#server_info-firstcall`, `3#server_info-only`; sessions: `800f50fc`.

### `nuget-upgrade-matrix-tool`
- **priority hint:** **low** — single-session, weak evidence; partial coverage already exists.
- **title:** Consider a `nuget_upgrade_matrix` tool for per-project upgrade eligibility
- **summary:** `f9c003f4` built a NuGet/TFM upgrade-eligibility matrix via raw `dotnet`/`Bash`; `get_nuget_dependencies` + `nuget_vulnerability_scan` + the `nuget-preflight` skill cover pieces but none emits a single per-project eligibility matrix. Weak (1 session) — listed for completeness, not urgency.
- **proposed action:** new tool (speculative) — `nuget_upgrade_matrix(workspaceId)`; validate demand before building.
- **evidence:** `2b#nuget-upgrade-matrix`; sessions: `f9c003f4`.

## 5. Meta-note

1. **Phase mix:** 9 included sessions = 2 refactoring / 6 release-operational / 1 planning-docs; but only **2** (`800f50fc` synthetic surface-test, `b70ec703` real refactor) did substantive semantic work — the other 7 reached roslyn only for a one-shot `server_info` readiness probe.
2. **Where friction concentrates:** **coverage of usage**, not reliability. The reliability surface is clean — one genuinely opaque error (`test_run`) and a documented-by-design staleness behavior (`PreviewTokenStale`); the rest of `800f50fc`'s errors are intentional negative probes the server handled correctly. The dominant problem is that roslyn is barely invoked.
3. **Repo skew:** genuine usage is almost entirely confined to **this repo**. The C#-heavy consumer repos (BioRemote 47, TradeWise 31, BioFileTransfer 19 sessions touching `.cs`) invoked roslyn **zero** times. So these findings are **dogfooding-limited, not scale-limited** — there is no evidence here about behavior on large/foreign solutions because the server was never pointed at one.
4. **One default-usage change for next time:** after the `server_info` readiness probe in refactor/remediation skills, actually `workspace_load` and route C# symbol edits through `find_references`/`rename_apply`/`compile_check` instead of `Edit`/`Grep`.
5. **Window adequacy:** 270 sessions is a **large** sample, so widening the window would **not** help — the thin signal reflects **low adoption**, not too-short a window. A more useful next step than a longer retro is a **targeted dogfooding pass**: deliberately use roslyn on a real BioRemote/TradeWise change to generate authentic usage-friction data, then re-run this retro against it.
