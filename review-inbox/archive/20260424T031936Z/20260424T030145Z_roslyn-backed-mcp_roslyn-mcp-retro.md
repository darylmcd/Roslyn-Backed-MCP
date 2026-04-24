---
generated_at: 2026-04-24T03:01:45Z
source_repo: roslyn-backed-mcp
source_repo_path: C:\Code-Repo\Roslyn-Backed-MCP
session_phase: refactoring (within a scripted deep-review audit)
---

# Roslyn MCP session retrospective — roslyn-backed-mcp — 2026-04-24

## 1. Session classification

**refactoring** — this session was an end-to-end execution of `ai_docs/prompts/deep-review-and-refactor.md` in `mode=full` against this repo. The dominant work was Roslyn MCP tool exercise (diagnostics, metrics, symbol analysis, flow, refactor preview/apply chains, build/test bundles, boundary probes) with two real refactoring applies landing on a disposable audit branch. The audit was the driver, but the tool surface being exercised *was* Roslyn MCP, so every finding belongs here.

## 2. Task inventory

Only file-modifying and non-trivial-command tasks — reads and pure tool probes are omitted.

| Task (one-line verb phrase) | Tool actually used | File type / domain | Right tool for the job? |
|---|---|---|---|
| Create throwaway worktree for Phase 6 isolation (rejected by sandbox) | `Bash git worktree add` | git lifecycle | ✓ native git |
| Create throwaway audit branch `audit/full-run-20260424` | `Bash git checkout -b` | git lifecycle | ✓ |
| Run `dotnet restore` to satisfy Phase 0 precheck | `Bash dotnet` | package restore | ✓ native dotnet |
| Load solution into MCP workspace | `mcp__roslyn__workspace_load` | Roslyn workspace | ✓ |
| Warm compilation cache | `mcp__roslyn__workspace_warm` | Roslyn workspace | ✓ |
| Sort `using` directives in CompileCheckService.cs | `organize_usings_preview` → `apply_with_verify` | C# semantic | ✓ Roslyn MCP |
| Append marker comment to DiagnosticsProbe.cs (exercise verify path) | `apply_text_edit(verify=true)` | C# text edit | ✓ for the probe intent |
| Commit Phase 6 applies to audit branch | `Bash git add/commit` | git | ✓ |
| Write draft audit report sections | `Write` / `Edit` | markdown | ✓ |
| Set benign `.editorconfig` key (CA9999 probe) | `set_editorconfig_option` | editorconfig | ✓ |
| Revert Phase 9 audit-only apply | `revert_last_apply` | Roslyn undo | ✓ |
| Manually strip CA9999 key (bijectivity bug workaround) | `Edit` | editorconfig | **missed opportunity — partial** — revert_last_apply only handles the most-recent apply; I had to hand-edit because other applies had since landed on top. Not a defect per se, but exposes a gap (see 2b) |
| Write canonical final audit report | `Write` | markdown | ✓ |
| Remove draft report | `Bash rm` | filesystem | ✓ |
| Sample 5 skill `.md` files for 16b audit | `Read` | markdown | ✓ |
| Grep backlog for regression cross-check | `Read ai_docs/backlog.md` | markdown | ✓ |

**No missed Roslyn-MCP opportunities** on C#-semantic tasks this session — the audit specifically pulled the Roslyn surface into every relevant slot. The one "partial miss" above is actually a gap in the revert surface, not a choice-of-tool error.

## 2a. Roslyn MCP issues encountered

**12 distinct issues** across the session. Verbatim quotes preserved where captured.

### 2a.1 — `scaffold_test_preview` emits invalid C#

- **Tool:** `mcp__roslyn__scaffold_test_preview`
- **Inputs:** `testProjectName="RoslynMcp.Tests", targetTypeName="RoslynMcp.Roslyn.Services.NamespaceRelocationService"`
- **Symptom:** generated test file body contains the fully-qualified name as the class identifier, which is a C# syntax error:
  > `public class RoslynMcp.Roslyn.Services.NamespaceRelocationServiceGeneratedTests : TestBase`
  
  also emits `new RoslynMcp.Roslyn.Services.NamespaceRelocationService();` against a type whose constructor takes DI arguments.
- **Impact:** Phase 12 apply chain `skipped-safety` — scaffolded test would not compile.
- **Workaround:** documented as P2 finding and left unapplied.
- **Repro confidence:** deterministic on any fully-qualified target-type name.

### 2a.2 — `find_dead_fields` misses compound-assignment writes

- **Tool:** `mcp__roslyn__find_dead_fields`
- **Inputs:** default scan over solution
- **Symptom:** `CompileCheckAccumulator.ErrorCount` / `WarningCount` / `CompletedProjects` returned as `usageKind=never-written, writeReferenceCount=0` despite `acc.ErrorCount++` at `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:155` (confirmed via a `rename_preview` diff).
- **Impact:** would have sent a dead-code agent down a false-positive trail; Phase 2 signal needed cross-verification.
- **Workaround:** manual cross-check via `rename_preview`.
- **Repro confidence:** deterministic for any field whose only writes are `++`/`--`/`+=`/`-=`.

### 2a.3 — `find_unused_symbols(includePublic=true)` false-positives every MCP SDK route

- **Tool:** `mcp__roslyn__find_unused_symbols`
- **Inputs:** `includePublic=true, limit=20`
- **Symptom:** all 20 results are `[McpServerPromptType]` / `[McpServerToolType]` / `[McpServerResourceType]` methods that the `ModelContextProtocol` SDK invokes reflectively. Convention-exclusion list covers EF / xUnit / ASP.NET / SignalR / FluentValidation but not MCP.
- **Impact:** every row in the public-unused scan was a false positive on this particular repo — meaning the feature is nearly unusable against any MCP server codebase.
- **Workaround:** documented as P3 finding.
- **Repro confidence:** deterministic on any `ModelContextProtocol` consumer.

### 2a.4 — `test_related_files` + `test_related` + `validate_workspace.discoveredTests` all empty for known-matching change

- **Tool:** `mcp__roslyn__test_related_files`, `mcp__roslyn__test_related`, `mcp__roslyn__validate_workspace`
- **Inputs:** changed file list `[CompileCheckService.cs, DiagnosticsProbe.cs]`; `metadataName=RoslynMcp.Roslyn.Services.CompileCheckService`
- **Symptom:** all three returned empty `tests` / `[]` / `discoveredTests=[]`, yet `test_discover(nameFilter=CompileCheck)` found **9 real test methods** in the same project.
- **Impact:** Phase 8 "scope tests to this change" workflow entirely empty; an agent following the documented bundle wouldn't know the 9 affected tests exist.
- **Workaround:** fell back to `test_discover` with nameFilter.
- **Repro confidence:** deterministic; matches existing backlog rows `test-related-files-empty-result-explainability` + `test-related-files-service-refactor-underreporting` (both P4).

### 2a.5 — `symbol_impact_sweep.suggestedTasks` under-reports scope count

- **Tool:** `mcp__roslyn__symbol_impact_sweep`
- **Inputs:** `IWorkspaceManager, summary=true, maxItemsPerCategory=10`
- **Symptom:** `suggestedTasks: ["Review 10 reference(s) — update call sites that assume the prior shape."]` despite `totalCount=193` references existing. The task string uses the *capped* count, not the true scope.
- **Impact:** an agent reading the sweep thinks 10 refs is the whole story; for a real rename across this interface it would be 193.
- **Workaround:** cross-check with `find_references(summary=true)` for `totalCount`.
- **Repro confidence:** deterministic when `summary=true` or `maxItemsPerCategory` truncates.

### 2a.6 — `validate_recent_git_changes` bare-string error envelope

- **Tool:** `mcp__roslyn__validate_recent_git_changes`
- **Inputs:** `runTests=false, summary=true` against current session (uncommitted branch-local edits from Phase 7)
- **Symptom (verbatim):** `"An error occurred invoking 'validate_recent_git_changes'."` — no `category`, `tool`, `message`, `exceptionType`, or `_meta` block. Every other erroring tool in the session returned the structured envelope.
- **Impact:** agent can't distinguish "git not on PATH" vs "solution outside a git repo" vs "unexpected internal error".
- **Workaround:** None — I used `validate_workspace` with the change-tracker set instead.
- **Repro confidence:** deterministic this session; root cause not confirmed.

### 2a.7 — `format_document_preview` returns "Document not found" after an auto-reload on the same call

- **Tool:** `mcp__roslyn__format_document_preview`
- **Inputs:** `filePath=.../NuGetVulnerabilityJsonParser.cs` immediately after a Phase 6 apply
- **Symptom (verbatim):** `"Invalid operation: Document not found: ...\\NuGetVulnerabilityJsonParser.cs. The workspace may need to be reloaded (workspace_reload) if the state is stale."` — `_meta.staleAction` = `"auto-reloaded"`, `_meta.staleReloadMs = 3260`. So the server *did* auto-reload but still returned the error.
- **Impact:** caller had to retry. The server already did the remediation; the call could have retried internally.
- **Workaround:** retry — succeeded on second attempt.
- **Repro confidence:** intermittent — reproduced once directly after a write.

### 2a.8 — `set_editorconfig_option` ↔ `get_editorconfig_options` round-trip asymmetry

- **Tool:** `mcp__roslyn__set_editorconfig_option` + `mcp__roslyn__get_editorconfig_options`
- **Inputs:** set `dotnet_diagnostic.CA9999.severity=none` on SampleLib file
- **Symptom:** set succeeds (`createdNewFile=false, key=..., value=none`), `grep` confirms the line was written to `.editorconfig`. Immediate get returns the 27 other options but **not the newly-set CA9999 key**.
- **Impact:** an agent verifying its own write by calling get would conclude the set failed.
- **Workaround:** read the `.editorconfig` file directly.
- **Repro confidence:** deterministic when the key targets an analyzer id no loaded analyzer reports.

### 2a.9 — `find_type_mutations` likely undercounts mutators on stateful class

- **Tool:** `mcp__roslyn__find_type_mutations`
- **Inputs:** `metadataName=RoslynMcp.Roslyn.Services.WorkspaceManager, limit=10`
- **Symptom:** only 2 mutating members reported (`GetSourceGeneratedDocumentsAsync` and `Dispose`, both `mutationScope=CollectionWrite`). WorkspaceManager's entire load/reload/close lifecycle mutates `_workspaces` — users would reasonably expect LoadAsync / ReloadAsync / CloseAsync to surface.
- **Impact:** pre-refactor blast-radius assessment understates impact.
- **Workaround:** cross-check with source read.
- **Repro confidence:** stable across reloads this session; observational until source-level verification.

### 2a.10 — `find_references_bulk` blows MCP output cap without `summary=true`

- **Tool:** `mcp__roslyn__find_references_bulk`
- **Inputs:** 2 symbols (`IWorkspaceManager` + `RefactoringService`), default params
- **Symptom (verbatim):** `"Error: result (120,504 characters) exceeds maximum allowed tokens. Output has been saved to ..."`. The tool's schema does not expose a `summary=true` flag (unlike its single-symbol sibling `find_references`), so a 2-symbol batch against a high-fan-out interface is unusable out of the box.
- **Impact:** the "bulk" ergonomic advantage is undercut on exactly the symbols where bulk matters.
- **Workaround:** fall back to N sequential `find_references(summary=true)` calls.
- **Repro confidence:** deterministic on any multi-symbol batch that includes a 100+-ref symbol.

### 2a.11 — `add_package_reference_preview` / `set_project_property_preview` produce collapsed XML

- **Tool:** `mcp__roslyn__add_package_reference_preview` and `set_project_property_preview`
- **Inputs:** `SampleLib`, `Newtonsoft.Json/13.0.3` and `LangVersion/latest`
- **Symptom:** preview diffs show `</PropertyGroup><ItemGroup>` and `<LangVersion>latest</LangVersion></PropertyGroup>` collapsed onto single lines with trailing whitespace. MSBuild still parses it, so it's cosmetic, but a reviewer sees ugly diffs.
- **Impact:** cosmetic — review friction.
- **Workaround:** post-apply format pass (not attempted).
- **Repro confidence:** deterministic.

### 2a.12 — `symbol_search("")` returns ~80 KB of everything

- **Tool:** `mcp__roslyn__symbol_search`
- **Inputs:** `query=""`
- **Symptom (verbatim):** `"Error: result (80,794 characters) exceeds maximum allowed tokens. Output has been saved to ..."`. Empty query = full index dump.
- **Impact:** would be confusing for an agent probing the query surface; also risks output-budget exhaustion.
- **Workaround:** None needed — I intentionally probed it. But under real usage, this is a foot-gun.
- **Repro confidence:** deterministic.

### 2a.13 (minor observational) — `_meta.heldMs` > `_meta.elapsedMs` on `workspace_reload`

- **Tool:** `mcp__roslyn__workspace_reload`
- **Symptom:** `heldMs=5998` vs `elapsedMs=3000` on the same call. Unclear which is the intended wall-clock for agents to budget against.
- **Impact:** minor — performance dashboards may disagree with each other.
- **Workaround:** pick one consistently (I used elapsedMs).
- **Repro confidence:** one-shot this session.

### 2a.14 — Host-process restarts between turns (client behaviour, not a server bug)

- **Tool:** all workspace-scoped tools
- **Symptom:** twice the `workspaceId` returned `Not found`, with `workspace_list.count=0`. Transparent stdio-host restart between conversation turns.
- **Impact:** each restart forced a ~4s reload + warm cycle. The error envelope was actionable but doesn't mention the restart possibility.
- **Workaround:** reload, re-warm, continue.
- **Repro confidence:** intermittent (twice in ~60 minutes of audit time).

## 2b. Missing Roslyn MCP tool gaps

### 2b.1 — "Revert specific apply by sequence number" (or by file-set)

- **Task:** after the Phase 7 `set_editorconfig_option` audit-only apply, and then two later applies (Phase 9 + its revert + more), I needed to remove the CA9999 line from `.editorconfig`. `revert_last_apply` only handles the most-recent apply; the workspace auto-reloaded between intervening calls had already cleared the undo stack.
- **Why Roslyn-shaped:** `workspace_changes` already tracks an ordered session-log of applies with `sequenceNumber`, files, and tool names. A targeted revert is a natural extension of that contract.
- **Proposed tool shape:** `revert_apply_by_sequence(workspaceId, sequenceNumber)` — restore the pre-apply file state for a specific entry in `workspace_changes`. Returns `{reverted: bool, revertedOperation: string, affectedFiles: [...]}`. Fails cleanly with "sequence not found" or "later applies depend on this one — revert them first" when the revert would conflict.
- **Closest existing tool:** `revert_last_apply` — LIFO only, and gets cleared by auto-reload.

### 2b.2 — "Scope-aware test discovery for multi-file refactors"

- **Task:** given files changed, discover ALL tests that exercise any symbol defined in those files — not just tests whose *name* overlaps with the filename.
- **Why Roslyn-shaped:** Roslyn already has the semantic graph. A reference-sweep over every public symbol in a changed file → any test method referencing any of those symbols → the real test set.
- **Proposed tool shape:** `test_discover_for_changes(workspaceId, changedFilePaths, mode: "nameOverlap" | "symbolReference" | "both")`. Or: extend `test_related_files` with a `mode` parameter exposing the reference-sweep variant as opt-in.
- **Closest existing tool:** `test_related_files` — its heuristic name-overlap falls over on service-layer renames where the service name is a substring of test fixtures, yet neither variant hit here.

### 2b.3 — "Diagnose workspace-not-found vs host-restart"

- **Task:** after a `workspaceId not found` error, distinguish "the host restarted, workspace is gone, reload needed" vs "the user called `workspace_close`" vs "something worse".
- **Why Roslyn-shaped:** the server already tracks `stdioPid` and `serverStartedAt` in `connection`. If an old `workspaceId` comes in against a different `stdioPid`, the server could annotate the error accordingly.
- **Proposed tool shape:** enrich the existing error envelope: `{ category: "NotFound", reason: "HostRestarted" | "WorkspaceClosed" | "EvictedByCap", recovery: "call workspace_load with the previously-loaded path" }`. Alternatively, a `diagnose_workspace_state(workspaceId)` tool that returns the best-guess cause.
- **Closest existing tool:** `server_heartbeat` — gives current state, but no history of when the id went away or why.

### 2b.4 — "Self-describing format/lint delta"

- **Task:** understand why `format_check` flags a file but `format_document_preview` returns an empty diff on the same file (see 2a.7 neighbour on `NuGetVulnerabilityJsonParser.cs` — format_check reported `changeCount=1`, the preview was empty).
- **Why Roslyn-shaped:** `format_check` already computes the delta; it could report *what kind* of change it found (trailing whitespace, newline at EOF, line-ending mismatch, etc.). Currently it returns an opaque `changeCount`.
- **Proposed tool shape:** extend `format_check` violations with `changeKinds: ["trailing-whitespace"|"crlf"|"final-newline"|"indent"|...]` per file; or add `format_diff_preview(filePath)` that shows exactly what the formatter would do, distinct from `format_document_preview` (which is already a no-op on line-ending-only diffs).
- **Closest existing tool:** `format_document_preview` — silent on the violations format_check flagged.

### 2b.5 — "Backlog-aware regression probe" (skill-shape, not tool)

- **Task:** during Phase 18 I had to manually read `ai_docs/backlog.md`, grep for related ids, and cross-reference against findings. A helper to enumerate open backlog rows and attempt auto-reproduction would save time on every audit.
- **Why Roslyn-shaped:** only loosely — this is closer to a skill composition than a tool gap.
- **Proposed shape:** a skill `audit-regression-probe` that parses the backlog rows marked P2/P3, attempts a representative repro per row, and emits a status column. Out of scope for a tool gap but worth noting.
- **Closest existing tool:** none.

## 3. Recurring friction patterns

### 3.1 — Auto-reload timing creates transient "not found" errors after writes

**What happened.** Two distinct calls immediately after a mutation returned `Document not found` with `staleAction=auto-reloaded` set (2a.7). The server already self-healed — but the caller still saw an error and had to retry.

**Why it recurs.** Any write-then-read pattern from the same turn crosses the reload boundary. Every Phase 6/7 apply is a candidate for this race.

**Fix:** when `staleAction=auto-reloaded` is about to fire on a missing-document lookup, retry the operation once against the refreshed snapshot before surfacing the error. The caller's cancellation token still caps total time. Alternative: set the error category to `StaleSnapshotRetryRequired` so the client knows the retry is safe.

### 3.2 — Test-relation heuristics drop obvious matches

**What happened.** 2a.4: `test_related_files`, `test_related`, and `validate_workspace.discoveredTests` all returned empty for a change to `CompileCheckService.cs` despite `test_discover(nameFilter=CompileCheck)` surfacing 9 real tests.

**Why it recurs.** It reproduces two open backlog rows (P4) and affects every service-layer refactor in this repo. Anyone following the documented Phase 8 / validate_workspace flow gets a silent empty set.

**Fix:** add the reference-sweep variant from 2b.2 and default `validate_workspace` to "both" modes — or at minimum, return a `warnings: ["no tests matched heuristic"]` so empty is distinguishable from "no tests exist".

### 3.3 — Dead-code classification has semantic blind spots

**What happened.** 2a.2 (compound-assignment writes missed) and 2a.3 (MCP SDK convention not on exclusion list). Both produce false positives in the public-unused scan.

**Why it recurs.** The dead-code family is used by the `dead-code` skill and by `suggest_refactorings` — both amplify the false positives. Every audit against an MCP consumer would hit 2a.3.

**Fix:** for 2a.2, count `++`/`--`/`+=`/`-=` and `Interlocked.*` as writes in `find_dead_fields`'s write-classification. For 2a.3, add the three MCP SDK attribute names to `excludeConventionInvoked`'s default list.

### 3.4 — High-fan-out symbols exceed the MCP payload cap silently

**What happened.** 2a.10 (`find_references_bulk` 120 KB), 2a.12 (`symbol_search("")` 80 KB), and the Phase-11 `get_di_registrations` default response (55 KB — handled via `summary=true` sibling). Caps trigger as a terminal error rather than as an auto-clamp.

**Why it recurs.** Every refactor against an interface with > ~100 refs hits this. Every bulk-operation is a candidate.

**Fix:** mirror `find_references(summary=true)` onto `find_references_bulk`; add `maxItemsPerSymbol` as well. For `symbol_search("")`, reject empty with an actionable error instead of returning everything.

### 3.5 — Error envelopes are excellent almost everywhere — with one conspicuous exception

**What happened.** Across the audit, error messages were **remarkably actionable** — `evaluate_csharp` timeout cites watchdog + grace + thread pool advice; `apply_text_edit` cites exact column-count mismatches; `move_type_to_file_preview` explains why nested types aren't extractable; `get_prompt_text` unknown-name lists the full 20-prompt catalog. But `validate_recent_git_changes` (2a.6) breaks the pattern with a bare string.

**Why it recurs.** Single outlier, but it's the one an agent will try when it has uncommitted changes — a high-usage path. The contract inconsistency matters more than any individual bug in that tool.

**Fix:** wrap the `validate_recent_git_changes` handler in the shared error-envelope decorator that every other tool uses. One-line change, probably.

## 4. Suggested findings (up to 5)

Ranked backlog-candidate findings for the Roslyn MCP maintainer's review. Not pushed anywhere — informational only.

### scaffold-test-preview-emits-invalid-cs

- **priority hint:** **high** — `generate-tests` skill's primary output is currently broken; any user following the skill dry-run will get non-compiling code.
- **title:** Fix `scaffold_test_preview` dotted-identifier class name + constructor-arg DI gap
- **summary:** Per 2a.1, passing a fully-qualified `targetTypeName` like `RoslynMcp.Roslyn.Services.NamespaceRelocationService` emits `public class RoslynMcp.Roslyn.Services.NamespaceRelocationServiceGeneratedTests : TestBase` (dots in identifier = CS syntax error) and `new NamespaceRelocationService();` against a constructor that requires DI args. The scaffolded file cannot compile. This cascades into the `generate-tests` skill (3.3-adjacent amplification via a user-invocable skill surface).
- **proposed action:** strip namespace prefix from emitted class name before inserting into the declaration; probe the target type's constructor and emit a TODO placeholder (or NSubstitute-shaped stub if present in test project package refs) for unresolvable args.
- **evidence:** `2a#scaffold_test_preview`, `3#skills-audit (§11 of the audit report)`

### dead-fields-missing-compound-assignment

- **priority hint:** **high** — false-positive classification on a field that is demonstrably written breaks the central "is this dead?" signal.
- **title:** Count `++` / `--` / compound-assignment as writes in `find_dead_fields`
- **summary:** Per 2a.2, `CompileCheckAccumulator.ErrorCount` / `WarningCount` / `CompletedProjects` are all reported `usageKind=never-written, writeReferenceCount=0`, yet CompileCheckService.cs:155 contains `acc.ErrorCount++;` etc. The rename_preview diff quoted the exact write site. The `dead-code` skill's preserve-list is thoughtful, but the detector itself underreports writes.
- **proposed action:** extend the write-reference sweep to include `++`/`--`/`+=`/`-=`/`*=`/`/=`/`<<=`/`>>=`/`|=`/`&=`/`^=` target-of-assignment semantics, plus `Interlocked.*` calls. Unit-test against a minimal fixture covering each operator.
- **evidence:** `2a#find_dead_fields`, `3#dead-code`

### find-unused-symbols-mcp-sdk-convention

- **priority hint:** **medium** — scoped to MCP-SDK consumers, but every row in this repo's `includePublic=true` scan is a false positive.
- **title:** Add `[McpServerToolType]` / `[McpServerPromptType]` / `[McpServerResourceType]` to `excludeConventionInvoked`
- **summary:** Per 2a.3, all 20 results of `find_unused_symbols(includePublic=true, limit=20)` in this repo are MCP SDK-attribute-invoked methods. The existing exclusion list already covers EF / xUnit / ASP.NET / SignalR / FluentValidation — the MCP attributes fit the same shape (reflected registration at host startup).
- **proposed action:** add the three attribute names to the default convention-invoked list; keep `excludeConventionInvoked=false` as the escape hatch.
- **evidence:** `2a#find_unused_symbols`, `3#dead-code`

### validate-recent-git-changes-error-envelope

- **priority hint:** **medium** — high-frequency surface (used whenever you have uncommitted changes); single-line fix.
- **title:** Wrap `validate_recent_git_changes` errors in the shared structured envelope
- **summary:** Per 2a.6 and 3.5, `validate_recent_git_changes` returned the bare string `"An error occurred invoking 'validate_recent_git_changes'."` — no `category`, `tool`, `message`, `exceptionType`, or `_meta`. Every other tool in the audit returned a structured envelope that agents can branch on. The contract inconsistency matters more than any individual failure mode.
- **proposed action:** apply the same error-envelope decorator (whatever `find_references` / `workspace_load` / `evaluate_csharp` use) to `validate_recent_git_changes`. Add a regression test covering the non-git-repo failure mode.
- **evidence:** `2a#validate_recent_git_changes`, `3#error-envelope-outlier`

### auto-reload-retry-inside-call

- **priority hint:** **medium** — a small UX win that compounds across every write-then-read turn.
- **title:** When `staleAction=auto-reloaded` fires, retry the operation once before returning error
- **summary:** Per 2a.7 and pattern 3.1, `format_document_preview(NuGetVulnerabilityJsonParser.cs)` returned `"Invalid operation: Document not found... workspace may need to be reloaded"` with `_meta.staleAction="auto-reloaded"`, `_meta.staleReloadMs=3260`. The server had already done the remediation; the caller just had to retry. This pattern will fire on any write-then-read turn.
- **proposed action:** in the staleness pipeline, after the auto-reload completes, retry the operation once against the refreshed snapshot. If the second attempt also misses, surface the original error (with `retriedAfterReload: true` for telemetry). Cancellation token caps total time.
- **evidence:** `2a#format_document_preview`, `3#auto-reload-retry`

## 5. Meta-note

This session was a deep-review audit (refactoring phase) of the Roslyn MCP server against itself — the cleanest lens on tool coverage this repo can produce. **Friction is concentrated in ergonomics (high-fan-out payload caps, auto-reload retry, round-trip asymmetry) and coverage (two dead-code false-positive classes, one scaffolding output defect) — not reliability**: across ~130+ tool invocations the server crashed zero times and the stable tier scored 100%. One behavioural change for next time: I'd reach for `find_references(summary=true)` before any bulk/impact tool on interfaces with > ~100 refs, since the payload-cap failure mode (2a.10, 2a.12) turned into retry overhead twice this session.
