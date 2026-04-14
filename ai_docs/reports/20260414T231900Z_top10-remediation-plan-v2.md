# Top 10 Remediation Plan v2 — 2026-04-14

**Status:** Awaiting human approval. No code written.

**Predecessor:** `20260414T220000Z_top10-remediation-plan.md` shipped via PR #150 — 9 P2/P3 items closed, 2 follow-up rows added.

**Source:** `ai_docs/backlog.md` Open work table (post-PR-#150).

**Selection criteria:**
1. Code correctness risk first.
2. Blast radius (how many tools/workflows affected).
3. Single-PR feasibility.

**Constraints applied:** Breaking changes acceptable. Boy-scout perf only on touched code. Cite measurements, not intuitions.

**Jellyfin baseline carryover (from `20260414T120000Z_jellyfin_stress-test.md` Phase 8 — pre-fix-pass numbers, still the relevant comparison until a re-run lands):**
- Lookup p95: 2517 ms (budget ≤2 s)
- Search p95: 4864 ms (budget ≤10 s)
- Analysis p95: 35553 ms (budget ≤30 s) — `project_diagnostics` exceeded; PR #150 added a result cache + analyzer-skip
- Mutation preview p95: 5512 ms (budget ≤15 s)

**Reality of this pass:** the previous PR closed all P2 unblockers. Remaining items skew toward perf, agent ergonomics, and missing features — none are correctness time-bombs. Selection therefore weights **(a) measurable perf wins on already-measured tools** and **(b) tractable agent-ergonomics features** over pure-docs items.

---

## Independent verification summary

| id | Backlog claim | Verification | Confidence |
|----|---------------|--------------|------------|
| `apply-with-verify-and-rollback` | Need atomic apply-then-verify-then-rollback | Confirmed gap. `ApplyRefactoringAsync` (`RefactoringService.cs:85-160`), `CompileCheckService.CheckAsync`, and `revert_last_apply` exist independently; no composite tool wraps them. The pattern recurs for every refactoring session. | High |
| `apply-text-edit-diff-quality` | Stray `;` / noisy diffs on edge probes | Diff path lives in `EditService.ApplyTextEditsAsync` → `DiffGenerator.GenerateUnifiedDiff`. Audit text is vague ("occasional"); needs concrete repro before code changes. **Action:** investigate first, then either fix or close as not-reproducible. | Low — investigation required |
| `dead-interface-member-removal-guided` | Composite tool to remove an interface member + all impls | Verified the 3 component tools exist (`find_unused_symbols`, `find_implementations`, `remove_dead_code_preview`). No composite. Implementation = a new orchestrator. | High |
| `di-registrations-scan-caching` | 12.1 s on Jellyfin; cache by `workspaceVersion` | Confirmed at `DiRegistrationService.cs:31-74`: full O(projects × syntaxTrees × invocations) scan on every call. No cache. PR #150's `DiagnosticService` cache is the template. | High |
| `long-running-tool-progress-notifications` | Long-running tools emit no `notifications/progress` | `ProgressHelper` exists at `src/RoslynMcp.Host.Stdio/Tools/ProgressHelper.cs` and is wired into 5 tools (`workspace_load`, scripting, validation, test-coverage). The 4 tools cited in the backlog (`project_diagnostics`, `test_run`, `nuget_vulnerability_scan`, `extract_and_wire_interface_preview`) **don't** have it — confirmed via grep. | High |
| `nuget-vuln-scan-caching` | 11.3 s on Jellyfin; shells `dotnet list package --vulnerable` | Confirmed at `NuGetDependencyService.ScanNuGetVulnerabilitiesAsync` (line 119-174). Every call shells out and parses fresh. Cache key: lockfile hash + `workspaceVersion` + `projectFilter` + `includeTransitive`. | High |
| `prompt-timeout-explain-refactor` | `explain_error` and `refactor_and_validate` prompts time out at ~25 s | Confirmed at `RoslynPrompts.cs:15-86` (`explain_error` calls `GetDiagnosticDetailsAsync` which goes through analyzer pipeline) and `RoslynPrompts.RefactoringWorkflows.cs:62-123` (`refactor_and_validate` calls `GetDiagnosticsAsync` with `severityFilter: null` → runs analyzers at Info floor). Both prompts wait for the slow path. PR #150's severity-Error short-circuit can apply here. | High |
| `scaffold-type-apply-perf` | 10.7 s `scaffold_type_apply` / 7.8 s `create_file_apply` | Root cause confirmed at `RefactoringService.PersistDocumentSetChangesAsync` line 528: `await _workspace.ReloadAsync(workspaceId, ct)` is called for **every** add/remove of a document. Reload is the 7-10 s cost; the file write itself is <100 ms. | High |
| `solution-project-index-by-name` | O(P) project lookups across services | Confirmed via grep — 6 callsites in `RoslynMcp.Roslyn/Services/`: `CrossProjectRefactoringService.cs:285`, `FileOperationService.cs:164`, `FixAllService.cs:237`, `ScaffoldingService.cs:127`, plus `ProjectFilterHelper.FilterProjects` (the central path). | High |
| `source-file-resource-line-range-parity` | `roslyn://workspace/{id}/file/{filePath}` returns whole file | Confirmed at `WorkspaceResources.cs:84-109`. Resource MIME is `text/x-csharp` — no JSON envelope, so a sibling URI template is the cleanest add. | High |

**Items considered but skipped from this batch:**
- `concurrent-mcp-instances-no-tools` (P3) — unknown root cause; needs investigation pass before a fix can be planned. Not single-PR feasible.
- `compilation-prewarm-on-load` (P4) — explicitly blocked on `concurrent-mcp-instances-no-tools` per its `deps` field.
- `agent-symbol-refactor-unified-preview` / `composite-split-service-di-registration` (P3) — both blocked on `roslyn-mcp-multi-file-preview-apply`.
- `string-literal-to-constant-extraction` (P3) — substantial new tool (SyntaxWalker + bulk replace + using-directive injection). Worth doing but doesn't fit the "10 small focused PRs" pattern.
- `mechanical-sweep-after-symbol-change` / `mapper-snapshot-dto-symmetry-check` (P3) — both effectively new heuristic tools; need design before code.
- `roslyn-mcp-claude-direct-tool-registration` (P3) — manifest change in `plugin/` repo, not in this codebase. Belongs to plugin-distribution PR.
- `schema-drift-jellyfin-audit` (P3) — bundled row; per its own note, should be split into per-tool rows before the next agent picks it up. Out of scope for code work.
- `prompt-appendix-drift` (P4) — pure docs sync; trivial but doesn't ship engineering value.
- `apply-text-edit-diff-quality` (P4) — original audit text is vague; needs repro before any code change.

---

## Implementation plan

Each item is intended as **one focused PR**. Suggested order: 1 (highest perf win) → 2, 3 (perf caches that share a pattern) → 4 (perf opt with lots of touch-points) → 5 (small feature) → 6 (UX) → 7 (perf-shaped bug) → 8, 9 (new tools) → 10 (investigation).

### 1. `scaffold-type-apply-perf` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | `RefactoringService.PersistDocumentSetChangesAsync` (`src/RoslynMcp.Roslyn/Services/RefactoringService.cs:464-536`) hits **`await _workspace.ReloadAsync(workspaceId, ct)`** at line 528 after every document add/remove. Reload re-loads the entire MSBuild solution from disk — Jellyfin baseline shows 9606 ms `workspace_load` cost. The file write at line 494 (`File.WriteAllTextAsync`) is <100 ms. The 10.7 s `scaffold_type_apply` (Phase 12) and 7.8 s `create_file_apply` are dominated by the reload. Same path is hit by `move_file_apply` for the add-then-remove sequence. |
| **Approach** | Replace the unconditional reload with a **try-incremental-then-fall-back-to-reload** path. (1) Persist files to disk as today (lines 479-525). (2) After persistence, attempt `_workspace.TryApplyChanges(workspaceId, modifiedSolution)` instead of reload. MSBuildWorkspace's `TryApplyChanges` supports `AddedDocuments`/`RemovedDocuments` for projects whose host model accepts the change. (3) If `TryApplyChanges` returns false (older Roslyn versions, certain project shapes), then fall back to `ReloadAsync` so behavior never regresses. (4) Bump `WorkspaceSession.Version` directly when `TryApplyChanges` succeeds, so per-`workspaceVersion` caches (`CompilationCache`, the result cache PR #150 added to `DiagnosticService`) invalidate correctly. (5) Keep the same return shape (`(bool Success, IReadOnlyList<string> AppliedFiles)`). |
| **Scope** | Modified: `RefactoringService.cs` (~20 LOC in `PersistDocumentSetChangesAsync`), `IWorkspaceManager.cs` (one new method or expose existing version-bump). New tests in `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs` and `tests/RoslynMcp.Tests/FileOperationIntegrationTests.cs`: assert `scaffold_type_apply` and `create_file_apply` complete in <2 s on the SampleSolution fixture, plus the existing semantic tests. New files: 0. Modified: 4. |
| **Risks** | (a) `TryApplyChanges` may silently drop document changes for some MSBuildWorkspace project hosts; the fallback reload guarantees correctness but the Success return must mean "actually applied" — verify by re-reading the workspace solution after `TryApplyChanges` returns true. (b) Folder structures (the `folders` argument to `AddDocument`) are preserved through `TryApplyChanges` per Roslyn docs, but verify in the integration test by looking at `Document.Folders` after the add. (c) The undo path (`_undoService?.CaptureBeforeApply`, line 106) takes a snapshot of `currentSolution` before the change — that already works regardless of which apply path runs. |
| **Validation** | Two new integration tests measuring `scaffold_type_apply` + `create_file_apply` wall time on SampleSolution. Existing `ScaffoldingIntegrationTests` and `FileOperationIntegrationTests` continue to pass (baseline correctness coverage). Manual: invoke `scaffold_type_apply` against a fresh Jellyfin workspace and confirm the apply completes in <2 s vs the 10.7 s baseline. |
| **Performance review** | Baseline (audit Phase 12): `scaffold_type_apply` 10713 ms, `create_file_apply` 7825 ms, both on Jellyfin. Expected after fix: `scaffold_type_apply` <500 ms (file-write dominated), `create_file_apply` <300 ms. Regression guard: integration test hard-caps at 2000 ms on SampleSolution. **20×–40× speedup** on the slow tools cited in the audit. |
| **Backlog sync** | Closes `scaffold-type-apply-perf`. Reduces severity of `apply-with-verify-and-rollback` slightly (one fewer slow apply path means the verify step is less of a bottleneck) but doesn't close it. |

---

### 2. `di-registrations-scan-caching` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | `DiRegistrationService.GetDiRegistrationsAsync` (`src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:31-74`) walks every project → every syntax tree → every `InvocationExpressionSyntax` looking for `AddSingleton`/`AddScoped`/`AddTransient` patterns. No cache layer. On Jellyfin (40 projects, 187 registrations) Phase 4.6 measured 12103 ms; scaled to 100 projects this approaches 30 s. Registrations only change when the workspace mutates (`apply_text_edit`, `workspace_reload`). The PR #150 result-cache pattern in `DiagnosticService` (per-`workspaceVersion` keyed cache, invalidated via `IWorkspaceManager.WorkspaceClosed`) maps cleanly here. |
| **Approach** | Add `ConcurrentDictionary<string, DiCacheEntry>` to `DiRegistrationService` keyed on `workspaceId`, value `(int Version, IReadOnlyList<DiRegistrationDto> All)`. On `GetDiRegistrationsAsync`, check `(workspaceVersion, projectFilter)`: cache hit → return cached slice; cache miss → run the full scan, store the unfiltered list, return the filtered slice. Subscribe to `IWorkspaceManager.WorkspaceClosed` in the constructor (same hook PR #150 used) to drop entries on workspace close. Bound at one entry per workspace (the unfiltered list is the canonical result; per-`projectFilter` slicing is computed cheaply on top). |
| **Scope** | Modified: `DiRegistrationService.cs` (~50 LOC). New tests in `tests/RoslynMcp.Tests/SemanticExpansionTests.cs` (or a new `DiRegistrationCacheTests.cs`): assert second call with same filter returns same DTO reference; assert `workspace_reload` invalidates; assert `WorkspaceClosed` purges the entry. New files: 0 or 1. Modified: 1 (+ tests). |
| **Risks** | (a) Concurrent writes on first call: use `GetOrAdd` with a `Lazy<Task<…>>` value so two concurrent first-callers share one scan instead of racing. The CompilationCache pattern is already in the codebase and is the exemplar. (b) The unfiltered list is workspace-wide; filtering by project name is O(results), trivial. (c) Memory: 187 DTOs × ~200 bytes ≈ 40 KB on Jellyfin. Cap at 1 entry per workspace, so total cache memory is bounded by `MaxConcurrentWorkspaces × ~40 KB` = ~320 KB at the default cap of 8. |
| **Validation** | Tests above. Manual: re-run Jellyfin Phase 4.6 (`get_di_registrations`); first call should still measure ~12 s, second call <100 ms. |
| **Performance review** | Baseline (Phase 4.6): 12103 ms. Cold call after fix: same ~12 s (full scan still required once). Warm call (cache hit) target: <50 ms. **240× speedup on cached calls; 0× change on the cold call** — but typical session usage hits the same workspace many times, so amortized savings on a 5-call session are ~9.5 s. |
| **Backlog sync** | Closes `di-registrations-scan-caching`. The `WorkspaceClosed` invalidation hook is shared with the `DiagnosticService` cache PR #150 added — both should subscribe via the same pattern, so this PR's approach is a continuation, not a divergence. |

---

### 3. `nuget-vuln-scan-caching` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | `NuGetDependencyService.ScanNuGetVulnerabilitiesAsync` (`src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:119-174`) shells out to `dotnet list package --vulnerable --format json` every call. Network round-trip to NuGet vulnerability DB dominates (~11 s baseline). Package references only change when `apply_project_mutation` adds/removes a `<PackageReference>`, when `Directory.Packages.props` changes (CPM), or when `workspace_reload` runs. |
| **Approach** | Add a per-workspace cache keyed on `(workspaceVersion, projectFilter, includeTransitive, lockfileHash)`. The lockfile hash is computed from the SHA-256 of `Directory.Packages.props` (or per-project `packages.lock.json` when CPM is off) — files that hash differently mean the package set changed even when `workspaceVersion` didn't tick (e.g. someone edited the props file outside the workspace). Cache entries live for the workspace lifetime; invalidate on `WorkspaceClosed`. Cap at 4 entries per workspace (typical `(true, false)` × `(null, projectName)` combos). |
| **Scope** | Modified: `NuGetDependencyService.cs` (~80 LOC for cache + hash helper). New tests in `tests/RoslynMcp.Tests/NuGetVulnerabilityScanIntegrationTests.cs`: cache hit returns same DTO reference; lockfile hash change invalidates. New files: 0. Modified: 2. |
| **Risks** | (a) Cache may go stale if the NuGet vuln DB updates server-side without local lockfile changes; that's an inherent trade-off — the cache shaves 11 s but means a freshly-disclosed CVE won't appear until the next `workspace_reload`. Document the trade-off and offer a `bypassCache: true` parameter for callers who must guarantee freshness. (b) Lockfile hash on a 100-project monorepo with no CPM: hashing each `packages.lock.json` adds <100 ms — still a 99% net win. (c) Multiple concurrent first-callers — use the same `Lazy<Task<…>>` pattern as item 2. |
| **Validation** | Tests above. Manual: invoke `nuget_vulnerability_scan` twice on Jellyfin; first should be ~11 s, second <100 ms. Modify `Directory.Packages.props`, third call should re-shell and again be ~11 s. |
| **Performance review** | Baseline (audit §6): 11336 ms. Cold call after fix: ~11 s + <100 ms hashing overhead. Warm call: <50 ms. **220× speedup on warm calls.** |
| **Backlog sync** | Closes `nuget-vuln-scan-caching`. |

---

### 4. `solution-project-index-by-name` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | Six callsites iterate `solution.Projects` to find one project by name (`CrossProjectRefactoringService.cs:285`, `FileOperationService.cs:164`, `FixAllService.cs:237`, `ScaffoldingService.cs:127`, `ProjectFilterHelper.FilterProjects:11`, `WorkspaceManager.cs:308`). On a 40-project solution each lookup is 40-element scan; on a 100-project solution it's 100. Per-call cost is sub-millisecond, but several services chain 5-10 lookups per request. The bigger win is API consistency — all callers should funnel through one helper so future perf or correctness fixes apply uniformly. |
| **Approach** | (1) Add `Project? GetProject(string workspaceId, string projectNameOrPath)` to `IWorkspaceManager` and implement it in `WorkspaceManager` with a per-`workspaceVersion` `Dictionary<string, Project>` index built lazily on first lookup. Index includes both name and full file path entries (case-insensitive). Invalidate on `IncrementVersion` (already called in `LoadIntoSessionAsync` and `ReloadAsync`). (2) Refactor the 6 callsites to use it. (3) Update `ProjectFilterHelper.FilterProjects` to use the index when `projectFilter` is non-null. |
| **Scope** | Modified: `IWorkspaceManager.cs`, `WorkspaceManager.cs`, `ProjectFilterHelper.cs`, plus the 5 service callsites. ~80 LOC total across files. New tests in `tests/RoslynMcp.Tests/WorkspaceLoadDedupTests.cs` (or a new `WorkspaceProjectIndexTests.cs`): assert lookup by name + by file path both return; assert lookup after `workspace_reload` returns the new Project instance, not the stale one. New files: 0 or 1. Modified: 7. |
| **Risks** | (a) The index must be invalidated at the same point `Solution.Projects` could change: load, reload, mutation. The version field on `WorkspaceSession` already covers all three. (b) Consumers expect throw-on-not-found (existing pattern: `?? throw new InvalidOperationException(...)`); preserve that at each callsite — `GetProject` returns `Project?`, callers wrap with their own throw to keep error messages in context. (c) Path-based lookups must normalize via `Path.GetFullPath` to match how callers compare. |
| **Validation** | Tests above + existing test suite. Manual: run `project_diagnostics` with `projectName=…` against Jellyfin; verify result is identical to pre-fix. |
| **Performance review** | Baseline: each lookup is O(P) — Jellyfin's 40 projects = ~40 string compares per call. Per-call cost <1 ms; not a hot path on its own. **Cumulative win** on a chained operation (e.g. `apply_project_mutation` → `compile_check` → `test_run` for 1 project) drops 6 lookups × 40 from ~6 ms to ~6 µs — measurable but not headline. **Real benefit:** one place to fix any future bug in project resolution (e.g. case sensitivity rules, file-path normalization). Tests assert lookup correctness, not the timing change. |
| **Backlog sync** | Closes `solution-project-index-by-name`. |

---

### 5. `source-file-resource-line-range-parity` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | The `roslyn://workspace/{id}/file/{filePath}` resource (`WorkspaceResources.cs:84-109`) returns the whole file as `text/x-csharp`. PR #150 closed the analogous tool (`get_source_text`) by adding `startLine`/`endLine`/`maxChars`. The resource is still full-file-only, so agents reading source via the resource pay full file cost (`EncodingHelper.cs` 384 KB even for a 30-line probe). |
| **Approach** | Add a sibling resource template: `roslyn://workspace/{workspaceId}/file/{filePath}/lines/{startLine}-{endLine}`. The `{startLine}-{endLine}` segment is decoded as two integers separated by a hyphen. Validate (`startLine >= 1`, `endLine >= startLine`, `endLine` clamped to the file end). Slice via the same `SliceLines` helper PR #150 added to `WorkspaceTools.cs` — extract that helper to a shared location (`RoslynMcp.Roslyn/Helpers/SourceTextSlicer.cs`) so both the tool and the resource use the same code path. Prepend a one-line `// roslyn://… lines N..M of T` comment marker to the response so agents always know which slice they got. The original whole-file resource stays unchanged for back-compat with whole-file reads (no breaking change). |
| **Scope** | New: `src/RoslynMcp.Roslyn/Helpers/SourceTextSlicer.cs` (~30 LOC, extracted from `WorkspaceTools.cs`). Modified: `WorkspaceResources.cs` (new method ~30 LOC, register URI template), `WorkspaceTools.cs` (call the shared helper instead of inline `SliceLines`), `Catalog/ServerSurfaceCatalog.cs` (declare the new resource template). New tests in `tests/RoslynMcp.Tests/WorkspaceResourceTests.cs`: valid slice, oversize-clamped, inverted-range error, non-numeric range parsing, marker prefix present. New files: 1. Modified: 4. |
| **Risks** | (a) MCP resource templates must be declared in the catalog so clients can list them — verify the new template appears in `roslyn://server/resource-templates`. (b) The `text/x-csharp` MIME type means errors can't go through the JSON envelope. Use the existing `McpToolException` pattern at `WorkspaceResources.cs:107-108` for not-found / invalid-range / out-of-bounds. (c) Path encoding rules from the existing resource (URL-encoded `filePath` segment) carry over unchanged. |
| **Validation** | Tests above. Manual: read `roslyn://workspace/{id}/file/<encoded-path>/lines/2021-3021` against Jellyfin's `EncodingHelper.cs` and verify response is ~30 KB with the marker comment. |
| **Performance review** | Baseline: full file (e.g. 384 KB for `EncodingHelper.cs`) regardless of intent. Expected after fix: payload sized to `endLine - startLine` lines, bounded by 65 KB cap (same as `get_source_text`). For the `EncodingHelper.cs` 30-line slice: 384 KB → ~30 KB (**~13× smaller** payload). Wall-clock dominated by JSON serialization, scales linearly with bytes. |
| **Backlog sync** | Closes `source-file-resource-line-range-parity`. The shared `SourceTextSlicer` helper is also a small refactor that PR #150's `SliceLines` (currently in `WorkspaceTools.cs`) should adopt. |

---

### 6. `long-running-tool-progress-notifications` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | `ProgressHelper.Report(IProgress<ProgressNotificationValue>?, float, float?)` already exists and is wired into 5 tools (`workspace_load`, scripting, validation, test-coverage). The 4 tools cited in the backlog — `project_diagnostics`, `test_run`, `nuget_vulnerability_scan`, `extract_and_wire_interface_preview` — don't take an `IProgress<>` parameter, so the MCP framework never opens a progress channel. Agents perceive these as silent hangs (35 s, 94 s, 11 s, 14 s respectively). |
| **Approach** | For each of the 4 tools: (1) add `IProgress<ProgressNotificationValue>? progress = null` to the `[McpServerTool]` method signature so the SDK auto-binds it. (2) Inside the tool's gate-wrapped lambda, call `ProgressHelper.Report` at meaningful phase boundaries — e.g. `project_diagnostics`: emit per-project progress (`Progress = projectIndex`, `Total = projectCount`); `test_run`: pipe `dotnet test` stdout through a regex matcher and emit on each test pass/fail line; `nuget_vulnerability_scan`: report `loading`, `executing-cli`, `parsing` phases; `extract_and_wire_interface_preview`: emit per-document scan progress. (3) Where the tool delegates to a service, lift the service signature to accept `IProgress<float>` (lighter, no MCP coupling) and adapt at the tool boundary. |
| **Scope** | Modified: 4 tool files (`AnalysisTools.cs`, `ValidationTools.cs`, `SecurityTools.cs` for nuget, `InterfaceExtractionTools.cs`), plus the 4 service files for the inner phase reports. Existing `ProgressHelper.Report` is the single emit primitive. ~150 LOC across the 8 files. New tests are difficult here — progress notification observation requires an MCP transport mock. Reasonable target: a unit test that the service variants accept the `IProgress<float>` and call it at least once during a real run on SampleSolution. New files: 0. Modified: 8. |
| **Risks** | (a) Over-reporting can saturate the MCP channel — debounce: emit at most every 250 ms or on every 5%-progress increment. (b) `test_run` stdout parsing depends on the test framework's reporter format; xUnit/MSTest/NUnit each look slightly different. Conservative parser matches the per-test-completion line shape `[xUnit.net 00:00:00.00] X "Y" PASSED|FAILED`. (c) `extract_and_wire_interface_preview` is single-method and finishes in 14 s on a small workload; coarse "starting/done" events may suffice. |
| **Validation** | Unit tests for the service-level `IProgress<float>` integration. Manual: invoke `project_diagnostics` from Claude Code against Jellyfin and observe progress notifications in the client (or in the stdio dump). |
| **Performance review** | N/A — UX fix, no hot-path changes. The added `ProgressHelper.Report` calls are sub-microsecond per emit. |
| **Backlog sync** | Closes `long-running-tool-progress-notifications`. |

---

### 7. `prompt-timeout-explain-refactor` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | Both prompts time out at ~25 s. Root cause confirmed in source: `RoslynPrompts.cs:15-86` (`explain_error`) calls `diagnosticService.GetDiagnosticDetailsAsync` — a path that goes through the analyzer pipeline on cold cache. `RoslynPrompts.RefactoringWorkflows.cs:62-123` (`refactor_and_validate`) calls `diagnosticService.GetDiagnosticsAsync(workspaceId, null, filePath, null, null, ct)` — file-filtered but no severity floor, so analyzers run for Info and below on the file's project. PR #150 added a Warning-floor short-circuit and a result cache to `DiagnosticService`; both can apply here once the prompts pass `severityFilter: "Warning"` and benefit from the cached path. |
| **Approach** | (1) `RoslynPrompts.cs:30` — change `diagnosticService.GetDiagnosticDetailsAsync` to use the cached path that PR #150 introduced. The detail-lookup cache exists, but the cache miss still pays the full analyzer cost; pre-fetch via `GetDiagnosticsAsync(severityFilter: "Warning")` first to warm the cache, then call detail lookup. (2) `RoslynPrompts.RefactoringWorkflows.cs:85` — pass `severityFilter: "Warning"` to `GetDiagnosticsAsync` so the analyzer pass is skipped when no Error-default analyzer is loaded (PR #150's short-circuit). (3) Wrap both prompt bodies in a `CancellationTokenSource` linked to the existing `ct` with a 20 s timeout, so a slow path is **observably** cancelled with a clear "the prompt aborted at the diagnostics step; reduce scope" error message instead of a generic 25 s framework timeout. |
| **Scope** | Modified: `RoslynPrompts.cs` (~30 LOC), `RoslynPrompts.RefactoringWorkflows.cs` (~30 LOC). New tests in `tests/RoslynMcp.Tests/PromptSmokeTests.cs`: assert `explain_error` and `refactor_and_validate` both return within 5 s on the SampleSolution fixture. New files: 0. Modified: 3. |
| **Risks** | (a) Warning floor hides Info-only diagnostics from the prompt's context summary; that's acceptable for a refactoring prompt (not for a "show me everything" prompt) — document in the prompt's `[Description]`. (b) The 20 s cancellation timeout is shorter than the framework's default; ensure the linked CTS is plumbed through the awaits and the cancellation message is informative. |
| **Validation** | New tests assert wall time. Manual: invoke both prompts via Claude Code against Jellyfin; both should return in <10 s vs 25 s baseline. |
| **Performance review** | Baseline (audit Phase 16): both prompts 25068 ms / 25081 ms. Expected after fix: `explain_error` <5 s (file-filtered Warning-floor), `refactor_and_validate` <8 s. **3-5× speedup**, returns within budget. |
| **Backlog sync** | Closes `prompt-timeout-explain-refactor`. |

---

### 8. `apply-with-verify-and-rollback` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | Verified the gap: there's no atomic apply-then-verify-then-rollback primitive. The pattern `*_apply` → `compile_check` → manual fix or `revert_last_apply` is universal across refactoring sessions. Each step is ~1-3 s; the 3-step round-trip is 6-10 s and includes the agent-side reasoning between steps. A composite tool collapses this to a single call. |
| **Approach** | Add a new tool `apply_with_verify(previewToken, rollbackOnError = true)` registered alongside the other apply tools (probably in a new file `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`). Implementation: (1) Resolve `workspaceId` from `previewToken` via `IPreviewStore.PeekWorkspaceId`. (2) Call `IRefactoringService.ApplyRefactoringAsync(previewToken, ct)`. (3) If apply failed, return `{ status: "apply_failed", error: ... }` immediately. (4) Capture pre-apply diagnostic count via `IDiagnosticService.GetDiagnosticsAsync(severityFilter: "Error")` — but this needs to happen BEFORE the apply, which means we need to bake the pre-count into the preview store at preview time, OR snapshot it just before apply within the apply-with-verify wrapper. (5) Run `ICompileCheckService.CheckAsync(workspaceId, new CompileCheckOptions(), ct)` after apply. (6) If new compile errors appear, call `_undoService.RevertLastApply(workspaceId, ct)` (existing tool path) and return `{ status: "rolled_back", errors: [...] }`. (7) Otherwise return `{ status: "applied", appliedFiles: [...] }`. |
| **Scope** | New: `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs` (~150 LOC), `src/RoslynMcp.Roslyn/Services/ApplyVerifyOrchestrator.cs` (~100 LOC if we factor out logic from the tool). Modified: `Catalog/ServerSurfaceCatalog.cs` (register tool), `ServiceCollectionExtensions.cs` (register orchestrator). New tests in `tests/RoslynMcp.Tests/ApplyWithVerifyTests.cs` (~150 LOC): apply-good-preview returns `applied`; apply-bad-preview triggers rollback; rollbackOnError=false returns `apply_failed_kept` with the broken state for inspection. New files: 3. Modified: 2. |
| **Risks** | (a) Pre-apply error count snapshot: comparing `pre.Errors` with `post.Errors` rather than `post.Errors > 0` matters for repos that already have errors — only NEW errors should trigger rollback. Implement by computing the diagnostic-id-and-location set before and after, and comparing membership. (b) Rollback may itself fail if the workspace was modified between apply and revert (unlikely under the workspace gate, but defensive). (c) `compile_check` runs on the whole solution — for a single-file refactor that's overhead. Optional: scope `compile_check` to the modified files' projects via `projectName=...`. |
| **Validation** | Tests above. Manual: produce a known-bad refactor preview (e.g. extract method with a forced var-redeclaration via a fixture), invoke `apply_with_verify`, assert `rolled_back` status + the original code is restored. |
| **Performance review** | Baseline: 3 separate tool calls × ~1-3 s each + agent-reasoning roundtrips ≈ 8-15 s wall-clock for a verify-then-fix loop. Expected after fix: single tool call ≈ 2-4 s (apply + compile_check). **3-4× speedup** and eliminates agent-side reasoning roundtrips. |
| **Backlog sync** | Closes `apply-with-verify-and-rollback`. |

---

### 9. `dead-interface-member-removal-guided` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | The 3 component tools exist (`find_unused_symbols`, `find_implementations`, `remove_dead_code_preview`) but no composite. Removing a dead interface member + its implementations requires manual handle threading across 3 tool calls. The Firewall-Analyzer remediation skipped the whole chain in favor of `StrReplace`. |
| **Approach** | Add a new orchestrator `RemoveInterfaceMemberOrchestrator` and a tool `remove_interface_member_preview(workspaceId, interfaceMemberHandle)` that: (1) Resolves the symbol from the handle. (2) Verifies it's an `IMethodSymbol`, `IPropertySymbol`, or `IEventSymbol` declared on an interface. (3) Calls `SymbolFinder.FindReferencesAsync` to confirm zero **non-implementation** call sites (callers other than `IsImplementationOfInterfaceMember(...)` matches). (4) Walks `SymbolFinder.FindImplementationsAsync` to gather every implementing class member. (5) Builds a single composite preview removing the interface declaration + every implementation in one shot. (6) Returns a preview token redeemable via `apply_composite_preview`. |
| **Scope** | New: `src/RoslynMcp.Roslyn/Services/RemoveInterfaceMemberOrchestrator.cs` (~200 LOC), `src/RoslynMcp.Host.Stdio/Tools/RemoveInterfaceMemberTool.cs` (~80 LOC). Modified: `ServiceCollectionExtensions.cs`, `Catalog/ServerSurfaceCatalog.cs`. New tests in `tests/RoslynMcp.Tests/DeadCodeIntegrationTests.cs`: dead interface method removal end-to-end on a fixture; live interface method (with at least one external caller) refuses to remove and returns the caller list. New files: 2. Modified: 2. |
| **Risks** | (a) Detecting "implementation only" callers: use `INamedTypeSymbol.FindImplementationForInterfaceMember` to enumerate impls, then exclude their declaration positions from the `FindReferencesAsync` results. Anything left is a real caller. (b) Default interface methods (DIM): the interface itself can have a body. Removing the DIM is fine only if every implementation either explicitly overrides it or doesn't rely on it; conservative behavior: refuse to remove DIM until the caller list is empty. (c) Generic interfaces: the implementations of `IMyInterface<T>` may differ per `T`; iterate via `INamedTypeSymbol.AllInterfaces` on each candidate type. |
| **Validation** | Tests above. Manual: against the SampleSolution add a dead `IAnimal.Bark()` member with one implementation, invoke `remove_interface_member_preview`, assert the preview removes both the interface line and the impl line; apply, verify compilation succeeds. |
| **Performance review** | N/A — new feature, baseline is "agent does it manually with `StrReplace`" which has no recorded timing. Expected: <2 s on the SampleSolution fixture, scales with the count of implementations × interface-member count. |
| **Backlog sync** | Closes `dead-interface-member-removal-guided`. Reduces the priority of the broader `mechanical-sweep-after-symbol-change` row (which covers a superset). |

---

### 10. `apply-text-edit-diff-quality` (P4) — **investigation pass first**

| Field | Content |
|-------|---------|
| **Diagnosis** | Backlog text is vague: "Occasional stray `;` or noisy diff text on line-replace probes — polish diff formatting for comments/edge columns." No concrete repro recorded. Diff path lives in `EditService.ApplyTextEditsAsync` → `DiffGenerator.GenerateUnifiedDiff`. The risk is that "polish" ends up being a wishlist of cosmetic preferences that don't actually fix anything. **Decision:** spend the first half of this PR producing a repro (run the existing edit-tools test fixtures with deliberately edge-case inputs and capture diffs), then either fix concrete issues or close the row as not-reproducible. |
| **Approach** | (1) Audit `tests/RoslynMcp.Tests/DiffGeneratorTests.cs` for coverage of: comment-only line replacements, replacements at the very end of a file (no trailing newline), replacements that span CRLF line-ending boundaries, single-character edits at column N. (2) Run `apply_text_edit` against the FirewallAnalyzer audit's exact probes (cited in `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §14) and capture the produced diffs. (3) For any diff that has a stray semicolon or unexpected whitespace, identify the root cause in `DiffGenerator` and fix. (4) If no concrete issue reproduces, **close the row as "not reproducible after PR #150 overlap-safety + syntax-preflight ship"** and move on. |
| **Scope** | Modified: `tests/RoslynMcp.Tests/DiffGeneratorTests.cs` (added repro cases ~50 LOC), possibly `src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs` (~20 LOC if a real fix is needed). If row is closed instead, scope shrinks to just a backlog edit. New files: 0. Modified: 1-2. |
| **Risks** | (a) The fix scope is unknown until the repro lands. If repro yields nothing actionable, the right outcome is **close the row, not invent a fix**. The plan explicitly calls for that escape hatch. (b) Diff cosmetics are subjective; only fix issues that are objectively wrong (e.g. extra characters that aren't in the source), not preferences (e.g. context line count). |
| **Validation** | New repro tests. If fixes land, the tests assert the corrected output. If row closes, the audit-cited issue is captured in a regression test that documents the now-passing behavior. |
| **Performance review** | N/A — diff formatting is sub-millisecond, no hot-path implication. |
| **Backlog sync** | Closes `apply-text-edit-diff-quality` — either by fix or by "not reproducible" closure note in the backlog row. |

---

## Cross-cutting observations

- **Pattern reuse from PR #150.** Items 2 (`di-registrations-scan-caching`) and 3 (`nuget-vuln-scan-caching`) both use the per-`workspaceVersion` `ConcurrentDictionary` cache pattern PR #150 introduced in `DiagnosticService`. Item 7 (`prompt-timeout-explain-refactor`) directly benefits from the Warning-severity short-circuit PR #150 added. Worth abstracting into a shared `WorkspaceVersionedCache<TKey, TValue>` helper if a third caching item lands after these — defer that abstraction until the third use case appears (rule of three).

- **Perf smells observed but NOT fixed in this batch:**
  - `SymbolRelationshipService.GetCallersCalleesAsync` (`SymbolRelationshipService.cs:191-225`) does parallel `SymbolFinder.FindReferencesAsync` calls but doesn't cache results across consecutive calls on the same symbol — relevant when an agent repeatedly inspects the same hot symbol. Add a backlog row `caller-callee-result-cache` (P4 perf) if this proves measurable on a real workload.
  - `WorkspaceManager.LoadIntoSessionAsync` (added by PR #150) calls `BuildProjectStatuses` which itself iterates `Projects.Documents` to count documents per project — O(D) per project. On 100+ project / 10k+ document solutions this is non-trivial. Not a hot path (only runs at load), but worth a backlog row `project-status-build-perf` if `workspace_load` time creeps above its current 9.6 s baseline.
  - `RoslynPrompts.cs:35` splits the entire file into lines via `sourceText.Split('\n')` — wasteful for 100 KB+ files when only ±5 lines around the diagnostic position are wanted. Item 7's fix covers `explain_error`'s timing but doesn't touch this allocation. Add row `prompt-source-context-line-slice` (P4 perf) if the prompt corpus grows.

- **One PR per item, 10 PRs total.** Even though items 2/3 share a cache pattern, they touch independent services with independent invalidation hooks; merging them would couple two unrelated rollouts. Keep separate per the standing "one PR = one story" rule.

- **Suggested merge order:** 1, 4 (foundational perf opts that other items don't depend on) → 2, 3 (cache items, share the pattern) → 5, 6 (small additive features, no risk to existing flows) → 7 (perf-shaped bug fix, depends on PR #150 cache being live in main) → 8, 9 (new tools that don't change existing semantics) → 10 (investigation; may close row instead of fixing).

---

## Final per-PR todo template

Each PR plan must include this block at the bottom:

```
- [ ] Implement fix per `ai_docs/reports/20260414T231900Z_top10-remediation-plan-v2.md` § <id>
- [ ] Tests pass: `just test`
- [ ] CI gate green: `just ci`
- [ ] Re-run Jellyfin baseline if perf-sensitive (items 1, 2, 3, 7)
- [ ] backlog: sync ai_docs/backlog.md (remove the closed row, update any cross-references called out in the plan's "Backlog sync" field)
```
