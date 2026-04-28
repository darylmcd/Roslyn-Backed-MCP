# Backlog sweep plan — 20260428T124405Z

**Generated:** 2026-04-28T12:44:05Z
**Backlog snapshot:** 2026-04-27T21:03:46Z
**Initiative count:** 7
**Anchor verification:** performed (in this session — 4 anchor-drift notes recorded inline)
**Addenda loaded:** yes (`ai_docs/prompts/backlog-sweep-addenda.md`)
**Skipped rows (3):**
- `dead-logger-fields-roslyn-services-batch-2` — deps `dead-logger-fields-roslyn-services-batch-1` unfinished
- `dead-logger-fields-roslyn-services-batch-3` — deps `dead-logger-fields-roslyn-services-batch-2` unfinished (transitive)
- `workspace-process-pool-or-daemon` — explicitly **Defer**ed pending worse-profile evidence

---

## Initiatives (in order)

### 1. change-signature-preview-metadataname-shape-error-actionability

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `change-signature-preview-metadataname-shape-error-actionability` |
| Diagnosis | `change_signature_preview` rejects fully-qualified names of shape `Namespace.Type.Method(string)` (parenthesized signature) with the vague error *"requires a method symbol; resolved null"*. The error misnames the cause (looks like resolution failure; actual cause is the parenthesized parameter list the locator does not accept) and gives no cure. The throw site is in the resolver path used by the change-signature service. Anchors: tool registration [src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs](src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs); service [src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs](src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs); locator resolver [src/RoslynMcp.Core/Models/SymbolLocator.cs](src/RoslynMcp.Core/Models/SymbolLocator.cs). All three files exist as cited. |
| Approach | Pick **Path B (cheap)**: improve the rejection message to name the shape mismatch and point at the supported form. Pre-check in the resolver: if `locator.MetadataName?.Contains('(')` is true AND the locator targets a method-style symbol, return an actionable error: *"metadataName must be a bare method name (e.g. 'Foo.Bar.Baz') with file/line/column for disambiguation, OR a symbolHandle from symbol_search; received '<input>' which contains a parenthesized signature. Drop the '(...)' or use symbolHandle."* Tighten the `[Description]` on the `metadataName` parameter in `ChangeSignatureTools.cs` to include a one-line shape example. |
| Scope | 2 production files: `ChangeSignatureService.cs` (or `SymbolLocator.cs`, depending on where the resolver lives — verify at execute time), `ChangeSignatureTools.cs` (parameter description). 1 test file: `tests/RoslynMcp.Tests/Services/ChangeSignaturePreviewMetadataNameShapeTests.cs` (new). Rule 3 fix/refactor cap (≤4 prod): satisfied. |
| Tool policy | `edit-only` |
| Estimated context cost | 28000 |
| Risks | If the parenthesized rejection path is shared with non-method symbol kinds (property indexer accessors), the new branch must not swallow legitimate symbols. Restrict the new check to method-style locators. The `[Description]` change ripples into the published tool catalog; if a snapshot/golden test exists, update its baseline. |
| Validation | New test calls `change_signature_preview` with `metadataName = "SomeType.SomeMethod(string)"` and asserts the error message contains `"parenthesized"` and `"symbolHandle"`. One positive test for the supported shape so the rejection branch's negation is exercised. Per-edit: `mcp__roslyn__compile_check` then `mcp__roslyn__test_run --filter ChangeSignaturePreviewMetadataNameShape`. CI-parity: `./eng/verify-release.ps1 -Configuration Release` before opening PR. |
| Performance review | N/A — correctness/UX fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed** — `change_signature_preview` rejection of parenthesized `metadataName` now names the shape mismatch and points at the supported form (bare method name + position, or `symbolHandle` from `symbol_search`). Closes `change-signature-preview-metadataname-shape-error-actionability`. |
| Backlog sync | Close rows: `change-signature-preview-metadataname-shape-error-actionability`. Mark obsolete: none. Update related: none. |

### 2. mcp-error-category-workspace-evicted-on-host-recycle

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `mcp-error-category-workspace-evicted-on-host-recycle` |
| Diagnosis | When the MCP stdio host gracefully recycles (PID change with `previousRecycleReason="graceful"`), every workspace-scoped tool call from the prior session returns a bare `KeyNotFoundException` with `category=NotFound` — indistinguishable from a typo'd `workspaceId`. Self-audit on 2026-04-27 observed this between Phase 1 and Phase 2. Workspace lookup throw site lives in [src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs](src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs) (hotspot — see addenda). Error envelope construction in [src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs](src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs) and structured-call middleware. **Anchor drift:** backlog cited `Tools/StructuredCallToolFilter.cs`; actual path is [src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs](src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs). Recycle-metadata source: [HostProcessMetadataStore.cs](src/RoslynMcp.Host.Stdio/Diagnostics/HostProcessMetadataStore.cs). **Anchor drift:** backlog cited `Tools/HeartbeatTools.cs`; actual path is [src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs](src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs) — `server_heartbeat` is a method on `ServerTools`, not a separate file. |
| Approach | Add a typed exception (`WorkspaceEvictedException` in `RoslynMcp.Core` or `RoslynMcp.Roslyn`) carrying `serverStartedAt` + `workspaceLoadedAt`. In `WorkspaceManager`, when a lookup misses but the manager has any record of the id whose `loadedAt < hostProcess.startedAt`, throw `WorkspaceEvictedException` instead of `KeyNotFoundException`. Decision point at execute: if `WorkspaceManager` wipes evicted-id history on recycle, add a small "evicted ids" set keyed during recycle so detection is exact (avoid the heuristic "id absent + history says we recycled" approach). In `ToolErrorHandler.cs` (and `StructuredCallToolFilter.cs` if needed), add a catch arm mapping `WorkspaceEvictedException` to `category="WorkspaceEvicted"` with both timestamps in the structured envelope fields. |
| Scope | 3 production files: `WorkspaceManager.cs` (throw site + optional evicted-ids set), `ToolErrorHandler.cs` (catch+map), new `WorkspaceEvictedException.cs` in `RoslynMcp.Core/Exceptions/` (or `RoslynMcp.Roslyn/Services/`). 1 test file: `tests/RoslynMcp.Tests/Services/WorkspaceManagerEvictionTests.cs` (new). Rule 3 fix/refactor cap (≤4 prod): satisfied. **Hotspot:** WorkspaceManager.cs (per addenda — schedule alone, no parallel-wave with other WorkspaceManager rows). |
| Tool policy | `edit-only` |
| Estimated context cost | 38000 |
| Risks | If consumers of `IWorkspaceManager` outside MCP tooling depend on `KeyNotFoundException` shape, they may regress. Run `find_references` on the throw site before changing. The mapping layer might convert `WorkspaceEvictedException` to `NotFound` upstream of `ToolErrorHandler` — confirm there is no broad `KeyNotFoundException` catch above the new throw site that would swallow the typed exception. Heuristic detection (without an evicted-ids set) is fragile if the manager wipes history; prefer the exact set. |
| Validation | New test constructs a manager with a forced `serverStartedAt` later than a registered workspace's `loadedAt`, requests that id, asserts envelope category=`WorkspaceEvicted` with both timestamps. Keep existing `NotFound` category test for never-registered ids. Per-edit: `mcp__roslyn__compile_check` + `mcp__roslyn__test_run --filter WorkspaceManagerEviction`. CI-parity: `./eng/verify-release.ps1`. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed** — workspace-scoped tool calls after a graceful host recycle now return `category="WorkspaceEvicted"` with `serverStartedAt` and `workspaceLoadedAt` in the envelope, distinguishing recycle-eviction from a typo'd `workspaceId`. Closes `mcp-error-category-workspace-evicted-on-host-recycle`. |
| Backlog sync | Close rows: `mcp-error-category-workspace-evicted-on-host-recycle`. Spin-off candidate: backlog row's anchor citations `Tools/StructuredCallToolFilter.cs` and `Tools/HeartbeatTools.cs` are stale — file an anchor-audit follow-up if the backlog auditor agent has not already caught this. |

### 3. symbol-info-not-found-message-locator-vs-location

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-info-not-found-message-locator-vs-location` |
| Diagnosis | When `symbol_info` is called with `metadataName` only (no file/line/column) and resolution fails, the error reads *"No symbol found at the specified location"* — but the caller did not supply a location. The misnaming makes diagnosis harder. Anchors: tool registration [src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs](src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs); resolver [src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs](src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs). Both files exist as cited. |
| Approach | Locate the throw/return that produces *"No symbol found at the specified location"* (likely in `SymbolNavigationService` or a shared resolver). Branch the message text on which locator field was supplied: `metadataName` only → *"No symbol found for metadata name '<name>'"*; `filePath`+`line`+`column` → *"No symbol found at <file>:<line>:<col>"*; `symbolHandle` only → *"No symbol found for symbol handle '<handle>'"*. If multiple fields are supplied, pick the most specific (location > handle > metadataName). Investigate at execute time whether the misnaming is shared with sibling navigation tools (`go_to_definition`, `goto_type_definition`); if yes, centralize at the resolver layer; if `symbol_info`-only, contain. |
| Scope | 1–2 production files: `SymbolNavigationService.cs` (or shared resolver). 1 test file: `tests/RoslynMcp.Tests/Services/SymbolInfoNotFoundMessageTests.cs` (new). Rule 3 fix/refactor cap: satisfied. |
| Tool policy | `edit-only` |
| Estimated context cost | 25000 |
| Risks | If resolver throws and a higher-up catch clobbers the message, the branch text won't surface — trace call path before editing. If the misnaming is shared with other tools and the fix is contained to `symbol_info`, sibling tools keep the bug — surface as a spin-off backlog row instead of widening this PR. Value formatting must safely handle null locator fields and trim long pasted `metadataName` to avoid leaking giant strings into the envelope. |
| Validation | Three new tests for the three locator shapes; assert the error text names the supplied field. Per-edit: `mcp__roslyn__compile_check` + `mcp__roslyn__test_run --filter SymbolInfoNotFoundMessage`. CI-parity: `./eng/verify-release.ps1`. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | **Fixed** — `symbol_info` not-found errors now name the locator field that was supplied (`metadataName` / `filePath:line:col` / `symbolHandle`) instead of the generic *"at the specified location"* text. Closes `symbol-info-not-found-message-locator-vs-location`. |
| Backlog sync | Close rows: `symbol-info-not-found-message-locator-vs-location`. Spin-off if discovered: a `navigation-tools-misnamed-locator-error` row for sibling tools sharing the bug. |

### 4. apply-composite-preview-name-friction

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `apply-composite-preview-name-friction` |
| Diagnosis | Tool name `apply_composite_preview` reads as a preview but the tool is **destructive** — it applies a previously-staged composite preview to disk. Catalog summary at [src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs:31](src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs:31) currently reads *"Apply a previously previewed orchestration operation."* — no destructive-warning marker, so agents reading `discover_capabilities` output do not see the warning. Tool wrapper at [src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs](src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs). The cheap fix is to lead the catalog summary with a destructive-warning marker; the rename to `composite_apply` would require a catalog version bump and is out of scope. |
| Approach | Edit the catalog summary at `ServerSurfaceCatalog.Orchestration.cs:31` to: *"DESTRUCTIVE — applies a previously-previewed orchestration operation to disk. Pair with a *_preview call in the same session."* Mirror the warning onto the `[Description]` attribute of the corresponding method in `OrchestrationTools.cs`. Add a regression test asserting `summary.StartsWith("DESTRUCTIVE")`. If a marker token precedent exists for other destructive tools, match it; otherwise set the precedent. |
| Scope | 2 production files: `ServerSurfaceCatalog.Orchestration.cs`, `OrchestrationTools.cs`. 1 test file: `tests/RoslynMcp.Tests/Catalog/CatalogDestructiveWarningTests.cs` (new or extend existing catalog test). **Rule 3 exemption: tool-surface-only, 2 files.** **Hotspot:** ServerSurfaceCatalog.Orchestration.cs (catalog partial — addenda lists the catalog family). |
| Tool policy | `edit-only` |
| Estimated context cost | 18000 |
| Risks | A snapshot/golden test of the full catalog will fail until baseline updated — include the snapshot diff in the PR. If any agent prompt or procedure greps the catalog summary for an exact string match (`"Apply a previously previewed orchestration operation"`), it will need updating — `Grep` for the literal across `ai_docs/` and `.claude/` before committing. README surface-count gate is unaffected (no count change). |
| Validation | New test `Catalog_AppliesDestructiveWarning_Apply_Composite_Preview` asserts `summary` prefix. Per-edit: `mcp__roslyn__compile_check` + targeted `mcp__roslyn__test_run`. CI-parity: `./eng/verify-release.ps1`. |
| Performance review | N/A. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed** — `apply_composite_preview` catalog summary and tool description now lead with a `DESTRUCTIVE` marker so agents reading `discover_capabilities` see the warning before invoking. Closes `apply-composite-preview-name-friction`. |
| Backlog sync | Close rows: `apply-composite-preview-name-friction`. Mark obsolete: none. |

### 5. dead-logger-fields-roslyn-services-batch-1

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `dead-logger-fields-roslyn-services-batch-1` |
| Diagnosis | Four services in `RoslynMcp.Roslyn.Services` declare `private readonly ILogger<T> _logger` and assign it from a constructor parameter, but no method ever reads `_logger`. Write count = 1 (ctor); read count = 0. Verified anchors at expected lines: [BulkRefactoringService.cs:16](src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:16), [CodeMetricsService.cs:14](src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs:14), [CompletionService.cs:13](src/RoslynMcp.Roslyn/Services/CompletionService.cs:13), [ConsumerAnalysisService.cs:14](src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:14). All four use traditional ctors, not C# 12 primary ctors as the row's prose implies; the fix shape is the same (drop field + ctor param + assignment). DI auto-resolves logger registrations via `AddLogging()`, so no `ServiceCollectionExtensions` change is required. |
| Approach | For each of the 4 services: delete `_logger` field declaration, remove `ILogger<T>` from ctor signature, remove `_logger = logger;` assignment. Drop `using Microsoft.Extensions.Logging;` if no other references. Run `mcp__roslyn__find_references` on each ctor before editing to surface any direct-`new` call sites in tests or DI helpers that would need the parameter list updated. |
| Scope | 4 production files: `BulkRefactoringService.cs`, `CodeMetricsService.cs`, `CompletionService.cs`, `ConsumerAnalysisService.cs`. 1 test file: `tests/RoslynMcp.Tests/Services/DeadLoggerFieldsTests.cs` (new — reflection-based assertion). Rule 3 cap (≤4 prod): exactly at the cap. |
| Tool policy | `edit-only` |
| Estimated context cost | 28000 |
| Risks | Test fixtures that construct services via direct `new BulkRefactoringService(..., NullLogger<BulkRefactoringService>.Instance)` will break when the parameter is dropped. Search test fixtures for direct `new` of any of the 4 service types and update those call sites in the same PR. If any DI registration uses `ActivatorUtilities.CreateInstance<T>` with explicit positional arguments, dropping the parameter shifts indices. |
| Validation | New test loops over `[BulkRefactoringService, CodeMetricsService, CompletionService, ConsumerAnalysisService]` and asserts `type.GetField("_logger", BindingFlags.Instance \| BindingFlags.NonPublic)` is null. Spot-check DI: `mcp__roslyn__get_di_registrations` after the change confirms the four services still resolve. CI-parity: `./eng/verify-release.ps1`. |
| Performance review | N/A. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | **Maintenance** — dropped never-read `ILogger<T>` field and constructor parameter from 4 `RoslynMcp.Roslyn.Services` types (`BulkRefactoringService`, `CodeMetricsService`, `CompletionService`, `ConsumerAnalysisService`). Batch 1 of 3. Closes `dead-logger-fields-roslyn-services-batch-1`. |
| Backlog sync | Close rows: `dead-logger-fields-roslyn-services-batch-1`. Update related: batches 2 and 3 are now unblocked for next sweep. |

### 6. di-graph-triple-registration-cleanup

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `di-graph-triple-registration-cleanup` |
| Diagnosis | `get_di_registrations(showLifetimeOverrides=true, summary=true)` reports 9 service types each registered 3 times across composition-root layers. `lifetimesDiffer=false` so no functional bug — last-write-wins — but 18 dead lines obscure intent. Cited examples: `ILatestVersionProvider`, `NuGetVersionChecker`, `ExecutionGateOptions`, `PreviewStoreOptions`, `ScriptingServiceOptions` (full set enumerable via `get_di_registrations` at execute time). Anchors: [src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs](src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs), [src/RoslynMcp.Host.Stdio/Program.cs](src/RoslynMcp.Host.Stdio/Program.cs). Any other composition fragments (e.g. `Host.Stdio/Extensions/ServiceCollectionExtensions.cs`) that call `services.Add*<T>()` for the cited types contribute. |
| Approach | (a) Run `mcp__roslyn__get_di_registrations` against the loaded workspace to capture the full duplicate list (verify 9 types and their identity). (b) For each duplicated type, decide an owning layer: Roslyn-internal services (`NuGetVersionChecker`, `ILatestVersionProvider`) → `RoslynMcp.Roslyn.ServiceCollectionExtensions`; options bound to config (`ExecutionGateOptions`, `PreviewStoreOptions`, `ScriptingServiceOptions`) → wherever the binding source lives. (c) Remove redundant `services.Add*` calls, leaving exactly one per type. (d) Preserve any `[Conditional]` or feature-flag-wrapped registration that is not actually redundant. Confirm `lifetimesDiffer == false` per type before each delete (the audit asserted this; verify per type to avoid flipping behavior). |
| Scope | 2–3 production files: `RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`, `Host.Stdio/Program.cs`, possibly `Host.Stdio/Extensions/ServiceCollectionExtensions.cs`. 1 test file: extend `tests/RoslynMcp.Tests/DependencyInjectionTests.cs` (or create if missing). Rule 3 cap (≤4 prod): satisfied. **Hotspot:** ServiceCollectionExtensions.cs (per addenda). |
| Tool policy | `edit-only` |
| Estimated context cost | 32000 |
| Risks | "Last write wins" assumed throughout the audit — if any duplicate registers a **different lifetime** the audit missed, removing the wrong one flips behavior. Verify lifetime equality per type before each delete. A duplicate may be a deliberate `TryAdd*` "default if not already registered" pattern — leave those, document why. If consumers test composition by mocking via a duplicated registration, those tests may break — run the full suite, not just the new assertion. |
| Validation | Build the production composition (`new ServiceCollection().AddRoslynMcp(...)` or whatever the public composition root is); per de-duped type, assert `services.Count(d => d.ServiceType == typeof(T)) == 1`. Keep the existing DI-resolution smoke test. CI-parity: `./eng/verify-release.ps1`. |
| Performance review | N/A. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | **Maintenance** — de-duplicated 9 service types each registered 3 times across composition-root layers. No functional change (last-write-wins; lifetimes were equal); 18 dead lines removed. Closes `di-graph-triple-registration-cleanup`. |
| Backlog sync | Close rows: `di-graph-triple-registration-cleanup`. Spin-off candidate: a DI-validation analyzer or test that flags duplicate registrations at compile time (separate initiative). |

### 7. coupling-metrics-p50-borderline-on-15s-solution-budget

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `coupling-metrics-p50-borderline-on-15s-solution-budget` |
| Diagnosis | `get_coupling_metrics` p50 was 12807ms on this 7-project / 571-doc solution during Phase 2 of the 2026-04-27 self-audit, against the 15s solution-scan budget. No regression vs prior runs but trending toward budget. Hypothesis: per-type symbol-walk re-enumerates the full solution instead of scoping `SymbolFinder` work to one project at a time when `projectName` is supplied. Anchors: service [src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs](src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs) (look for `GetCouplingMetricsAsync`), tool registration [src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs](src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs) (`GetCouplingMetrics`). Both files exist as cited. |
| Approach | (a) **Profile first** — capture a baseline via stopwatch test or BenchmarkDotNet (preferred if a Benchmarks project exists); record the dominant cost in `GetCouplingMetricsAsync`. (b) Apply the smallest scoping change that respects `projectName`: when non-null, restrict the project loop to that one project; skip cross-project resolution unless data shape requires it. (c) When `projectName` is null, evaluate whether `Parallel.ForEachAsync` over projects is safe (Roslyn `Compilation` is thread-safe for read-only ops; verify for `SymbolFinder`). (d) Re-run the benchmark; record before/after in PR description. **If the dominant cost is workspace warm-up rather than the symbol walk, scoping won't help — surface a follow-up backlog row and exit, still shipping the profiling artifact.** |
| Scope | 1–2 production files: `CodeMetricsService.cs` (scoping change), optionally `AdvancedAnalysisTools.cs` (none expected unless the projectName plumbing changes). 1 test file: `tests/RoslynMcp.Tests/Benchmarks/CouplingMetricsBenchmarks.cs` (BenchmarkDotNet harness, if a Benchmarks folder exists; OR a stopwatch-asserting xUnit test under `tests/RoslynMcp.Tests/Performance/CouplingMetricsPerfTests.cs`). Rule 3 cap (≤4 prod): satisfied. Rule 4 cap (≤3 test files): satisfied. |
| Tool policy | `edit-only` |
| Estimated context cost | 55000 |
| Risks | Parallelism inside an MCP request may race on shared logger/options state if the service holds non-thread-safe fields. Inspect the field set before introducing `Parallel.ForEachAsync`. Wall-time perf assertions are flaky on shared CI runners — use a generous bound (or skip-on-CI flag) if BenchmarkDotNet is unavailable. The profiling step gates the rest of this work — if results contradict the hypothesis, a smaller fix may be inadequate; surface a spin-off rather than over-scope. |
| Validation | New benchmark or perf-test asserts per-project coupling on a small workspace completes in <2s. End-to-end re-run of the Phase 2 self-audit shape on this repo's solution; record p50 before/after, target <8s (50% of budget). Per-edit: `mcp__roslyn__compile_check` + targeted `mcp__roslyn__test_run`. CI-parity: `./eng/verify-release.ps1`. |
| Performance review | **Yes** — required. Capture baseline + after numbers in PR description; include the benchmark/profile artifact. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed** — `get_coupling_metrics` per-project execution path now scopes the symbol walk to the supplied `projectName` (when set), reducing p50 latency on multi-project solutions. Baseline 12807ms → target <8000ms on the 7-project / 571-doc reference solution. Closes `coupling-metrics-p50-borderline-on-15s-solution-budget`. |
| Backlog sync | Close rows: `coupling-metrics-p50-borderline-on-15s-solution-budget`. Spin-off candidate: if the dominant cost turns out to be workspace warm-up rather than the symbol walk, file a row for that. |

---

## Hotspot distribution check

| Order | Initiative | Hotspot? | Hotspot file |
|---|---|---|---|
| 1 | change-signature | no | — |
| 2 | workspace-evicted | yes | `WorkspaceManager.cs` |
| 3 | symbol-info-locator | no | — |
| 4 | apply-composite | yes | `ServerSurfaceCatalog.Orchestration.cs` |
| 5 | dead-logger-batch-1 | no | — |
| 6 | di-graph-cleanup | yes | `ServiceCollectionExtensions.cs` |
| 7 | coupling-metrics | no | — |

No two adjacent-order initiatives both touch hotspot files. Parallel-mode wave picker can safely batch (1, 3, 5, 7) and (2, 4, 6) — though the addenda's "≤1 hotspot per wave" rule means wave 2 should still serialize.

## Self-vet checklist (Step 7)

- [x] No initiative claims to close more than 1 row. Rule 1 N/A (no bundles).
- [x] No initiative touches more than 4 production files (Rule 3 cap).
- [x] No initiative adds more than 3 test files (Rule 4 cap).
- [x] No initiative's `estimatedContextTokens` exceeds 80K (Rule 5 cap; max is coupling-metrics at 55K).
- [x] Every initiative has explicit `toolPolicy: "edit-only"` (no nulls; no `preview-then-apply` initiatives in this batch).
- [x] No two adjacent-`order` hotspot-touching initiatives (verified above).

## Next step

Run `/backlog-sweep:review` before `/backlog-sweep:execute` to get an adversarial vet of the plan. Then `/backlog-sweep:execute` to ship the lowest-cost initiative (or `count=2` for a parallel wave with the disjoint pair (1, 3)).
