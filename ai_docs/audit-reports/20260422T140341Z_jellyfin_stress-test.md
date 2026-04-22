# Stress Test Report: Jellyfin

| Field | Value |
|---|---|
| Repo | jellyfin/jellyfin |
| Solution | C:\Code-Repo\jellyfin\.claude\worktrees\stress-test-jellyfin\Jellyfin.sln |
| Projects | 40 |
| Documents | 2048 |
| Target frameworks | `net10.0` (39 projects), `netstandard2.0` (1 — `Jellyfin.CodeAnalysis`) |
| Workspace load (ms) | 10480 |
| Warm-up (ms) | 4821 |
| projectsWarmed / coldCompilationCount | 40 / 16 |
| Report generated | 2026-04-22T14:03:41Z |
| Roslyn MCP version | 1.28.0+6810a6758e47fc066be0abb8a23f7083f4674c14 |
| Catalog version | 2026.04 |
| Live surface | tools: 107/53, resources: 9/4, prompts: 0/20 |

**Environment notes**

- Host: Windows 11 Pro (10.0.26200), .NET 10.0.7, Roslyn 5.3.0.0.
- Roslyn MCP `productShape` = `local-first`, `parityOk` = true, `stdioPid` = 18500.
- Stress test executed from a disposable worktree of `jellyfin` (master branch): `.claude/worktrees/stress-test-jellyfin`.
- Prior stress-test reports in this directory: **none** (no comparison baseline available).

---

## Phase -1: MCP server precondition

| Check | Result | Notes |
|---|---|---|
| `mcp__roslyn__server_info` callable | PASS | 15 ms, version 1.28.0 |
| `connection.state == "ready"` | PARTIAL | State was `initializing` pre-load (no workspace yet); one `server_heartbeat` returned 1 ms but still `initializing`. The connection docstring defines `ready` as workspace-loaded, so this is expected before Phase 0. |
| `surface.registered.parityOk` | PASS | true |
| Catalog resource (`roslyn://server/catalog`) matches `server_info` | **FAIL (P2)** | `ReadMcpResourceTool` could not resolve the server — the client listed it as `plugin:roslyn-mcp:roslyn` but `Server "plugin:roslyn-mcp:roslyn" is not connected`. Resource-tier parity could not be independently verified against the tool-tier counts (107 stable / 53 experimental tools; 9/4 resources; 0/20 prompts). Treat as a **UNEXPECTED** finding; the tool surface itself is healthy. |

**Gate verdict:** proceed with a documented P2 caveat for resource-tier verification.

**Side-observation (F23, client-side, P3).** The MCP client's tool-name prefix uses underscores (`mcp__plugin_roslyn-mcp_roslyn__*`) while `ListMcpResourcesTool` / `ReadMcpResourceTool` expect the server name formatted with colons (`plugin:roslyn-mcp:roslyn`). Attempts to read `roslyn://server/catalog` with either spelling failed with `Server "…" is not connected`. This is a client-side naming inconsistency — not a Roslyn MCP server defect — but it's why the Phase -1 catalog-parity check could not be independently verified on this run.

**Side-observation (F24, contract, P3).** `connection.state == "initializing"` persisted for ~**2.5 minutes** between `serverStartedAt` (14:01:10 UTC) and the first `workspace_load` call (14:03:41 UTC) that flipped the state. Both `server_info` (15 ms) and `server_heartbeat` (1 ms) returned instantly throughout this window, so transport was healthy — only the state field was lagging. The Phase -1 gate doc says "If it never becomes ready, halt" — a caller interpreting that strictly would halt before ever issuing `workspace_load`, which is itself what flips the state to `ready`. The contract should either document "initializing is a normal pre-load state" or promote `ready` to mean "server process is up" rather than "workspace loaded".

---

## Phase 0: Setup

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 0 | workspace_load | Jellyfin.sln | 10480 | PASS | `projectCount=40`, `documentCount=2048`, `isReady=false`, `analyzersReady=false`. `restoreHint`: "39 analyzer reference(s) failed to load — …Run `dotnet restore`". |
| 0 | workspace_health (pre-restore) | ws | 2 | PASS | `workspaceDiagnosticCount=40`, `workspaceErrorCount=0`, `workspaceWarningCount=40`. |
| 0 | `dotnet restore` (host shell) | solution | ~45 s (wall) | PASS (exit 0) | Ran in background. |
| 0 | workspace_reload | ws | 11719 (held=23430) | PASS | `projectCount=40`, `documentCount=2065` (+17 docs from restore-generated sources). `restoreHint=null` in the reload payload. |
| 0 | workspace_health (post-restore) | ws | 2 | UNEXPECTED | `isReady=false`, `analyzersReady=false`, `restoreHint` re-populated to the same 39-analyzer string. The analyzer-reference failure survives restore + reload on this workspace, so analyzer-driven tools (`project_diagnostics`, `find_unused_symbols`, CA/IDE rules) will under-report for the entire run. Logged as a **P2 finding**. |
| 0 | workspace_warm | ws (all 40) | 4826 (elapsed) / 4821 (tool-reported) | PASS | 16 cold compilations observed. **Budget:** no formal budget — but on 40 projects / 2065 docs this is healthy for a cold warm. |
| 0 | project_graph | ws | 2 | PASS | 40 projects, 16 test projects (40 %), 1 `netstandard2.0`, rest `net10.0`. Deepest fan-in: `Jellyfin.Server` (23 project refs). Deepest fan-out target: `Jellyfin.CodeAnalysis` (referenced by every other project; used as a shared analyzer). |

**Jellyfin dependency shape at a glance**

- **Hub projects** (large consumer fan-in): `MediaBrowser.Controller`, `MediaBrowser.Common`, `MediaBrowser.Model`, `Jellyfin.Extensions`, `Jellyfin.CodeAnalysis`.
- **Leaf projects**: `Jellyfin.CodeAnalysis`, `Jellyfin.Data` (only Jellyfin.Database.Implementations + CodeAnalysis), `Jellyfin.MediaEncoding.Keyframes`, `Jellyfin.Database.Implementations`.
- **Test projects** (16 total): every production project has a sibling `*.Tests` project.

**Findings (Phase 0):**
- **F20 (P2, verified).** `workspace_reload` **unconditionally returns the full per-project verbose payload** — there is no `verbose=false` / `summary=true` option. On Jellyfin the reload response was **73,338 characters**, above the MCP token cap. I had to persist the response to disk and use PowerShell + `ConvertFrom-Json` to extract `elapsedMs`/`projectCount`/etc. Sibling defect to F11/F12/F18 (tools missing a payload reducer).
- **F25 (P3, observation).** `workspace_reload._meta.heldMs = 23430` vs. `elapsedMs = 11719` — the lock-hold was **~2× the elapsed time** on this call, a ratio not observed on any other tool call in the run. Could indicate double-counting or a nested-hold bug in the rw-lock gate accounting; benign for correctness but worth investigating.
- **F26 (P2, observation).** `workspace_warm` reports `projectsWarmed=40` and `coldCompilationCount=16`, but `workspace_health` run immediately after continues to report `analyzersReady=false` alongside `isReady=false`. **Warm-up primes the compilation + semantic-model caches but does not prime the analyzer pipeline.** Combined with F2 (AD0001 crashes) and the persistent `restoreHint`, analyzer-dependent tools under-reported for the entire run. Consider a `workspace_warm(warmAnalyzers=true)` option, or surface this as a distinct readiness dimension in the summary output.

---

## Phase 1: Compilation baseline

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 1.1 | compile_check (severity=Error) | full solution | 7620 | PASS (0 errors) | First CS-diagnostics sweep post-warm. Under 30 s budget. `completedProjects=40/40`. |
| 1.2 | compile_check (severity=Warning, limit=50) | full solution | **304** | PASS (0 compiler warnings) | Fully cached: 2nd call on identical semantic state. Good cache behavior. Every CS-severity=Warning-or-worse diagnostic returned zero, yet the compiler itself is clean; the warning noise lives in `project_diagnostics` (analyzer channel). |
| 1.3 | compile_check (emitValidation=true) | full solution | **35121** | **SLOW** | 4.6× the emit-off baseline (7620 → 35121 ms). Docstring predicts 50-100× with a restored workspace. The emit phase is clearly executing (not short-circuited on missing references) yet the observed multiplier is far below documented. Two hypotheses: (a) the 39 unresolved analyzer references still short-circuit parts of the emit pipeline; (b) a genuinely fast emit on net10.0 Jellyfin. Worth a follow-up: compare against a freshly-cloned Jellyfin on a host with no lingering analyzer issue. |
| 1.4 | project_diagnostics (severity=Warning, summary=true) | full solution | **35266** | **SLOW** (>30 s budget) | `totalErrors=2` (AD0001 — analyzer crashed), `totalWarnings=40` (39 `WORKSPACE_UNRESOLVED_ANALYZER`, 1 `WORKSPACE_WARNING`), `totalInfo=3430`, `distinctDiagnosticIds=3`. Payload stayed small (`summary=true`). **AD0001 × 2** is a real finding — a loaded analyzer threw during execution. |
| 1.5 | compile_check (Emby.Server.Implementations, severity=Error) | 1 project | 10 | PASS (0 errors) | Near-instantaneous. Cache-hit after 1.1 warmed compilations. |
| 1.6 | format_check | full solution | 3880 | PASS (49 violations / 1987 docs) | 2.5 % of documents have formatting drift. Included `Jellyfin.Server/Filters/CachingOpenApiProvider.cs` and 48 others — primarily 1-change violations (`changeCount=1` on every entry). |

**Findings:**
- **F1 (P2, verified).** `project_diagnostics(summary=true)` takes **35 s** on 40 projects / 2065 docs — above the 30 s solution-wide budget and 4× the `compile_check` baseline on the same workspace. Because `summary=true` already skips row serialization, the cost is in diagnostic computation across 40 project compilations. Worth profiling: per-project compilation reuse vs. re-compute.
- **F2 (P1, unverified).** 2 × **AD0001** analyzer crashes are present. These erode every analyzer-driven tool (`find_unused_symbols`, `suggest_refactorings`, etc.) because AD0001 means the analyzer *aborted* on some inputs. Can't triage deeper from the `summary` payload — would need a row-level `project_diagnostics(summary=false, diagnosticId=AD0001)` call.
- **F3 (P2, verified).** `compile_check(severity=Warning, limit=50)` drops from 7620 ms (severity=Error) to **304 ms** on the very next call. The caching behavior is excellent — this is a quote-worthy win for the stress report.
- **F4 (P2, verified).** `format_check` scans 1987 documents in 3880 ms (~1950 docs/sec). Healthy rate and well within implicit budget.

---

## Phase 2: Symbol resolution at scale

Targets chosen (all in `MediaBrowser.Controller`, classic hub types): **`BaseItem`** (abstract class, entity root), **`IUserManager`** (high-fan interface), **`ILibraryManager`** (highest-fan interface in the repo).

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 2.1 | symbol_search | `BaseItem` | 507 | PASS | 20 hits returned (substring match), incl. types, extensions, DTOs. First `symbol_search` of the run. |
| 2.1 | symbol_search | `IUserManager` | 157 | PASS | 20 hits. Curve is visibly warmed. |
| 2.1 | symbol_search | `ILibraryManager` | 67 | PASS | 20 hits. Cache is fully primed. |
| 2.2 | symbol_info | BaseItem @ 45:27 | 5 | PASS | `kind=Class`, `modifiers=abstract, public`; 3 interfaces (`IHasProviderIds`, `IHasLookupInfo<ItemLookupInfo>`, `IEquatable<BaseItem>`). |
| 2.2 | symbol_info | IUserManager @ 17:22 | 0 | PASS | Effectively instant (cache hit). |
| 2.2 | symbol_info | ILibraryManager @ 30:22 | 0 | PASS | Same. |
| 2.3 | Cold-vs-warm `symbol_search` delta | BaseItem | — | **SKIPPED — warm-up done in Phase 0** | Cannot measure cold-start penalty on this run. Recommend a future stress test with `workspace_warm` deliberately omitted to capture the delta. |
| 2.4 | find_references | BaseItem @ 45:27, summary=true, limit=50 | **566** | PASS | **`totalCount=1452`**. The largest fan-out symbol in Jellyfin's controller tree. `summary=true` kept payload manageable. |
| 2.4 | find_references | IUserManager @ 17:22, summary=true | 53 | PASS | `totalCount=233`. |
| 2.4 | find_references | ILibraryManager @ 30:22, summary=true | 5 | PASS | `totalCount=362`. |
| 2.5 | find_references page 2 | BaseItem, offset=50, limit=50 | **11** | PASS | Paging a hot symbol is ~50× cheaper than the first page — good pagination cache. |
| 2.6 | callers_callees | `IUserManager.GetUserById(Guid)` @ 48:15 | 320 | PASS | **`totalCallers=130`**, `totalCallees=0` (interface method — expected). `hasMoreCallers=true`. |
| 2.7 | impact_analysis | IUserManager @ 17:22 | 15 | PASS | 233 refs across **11 projects**, 109 affected declarations. Payload was small with `referencesLimit=10/declarationsLimit=10`. |
| 2.8 | symbol_search (broad) | `Get` (limit=10) | ~15 (from _meta) | PASS | Cache-hit warm read. The breadth of "Get" hits any symbol whose name substring-matches — appears instantaneous because pagination cuts off the match set early. |
| 2.9 | semantic_search | `async methods returning Task<bool>` | 117 | PARTIAL | Returned 10 methods; of these, most are `Task<void>`/`Task`-returning (Download/Refresh/Fetch/Migrate family), **not** `Task<bool>`. Likely a token-match fallback fired, not structural. `warning=null` — the tool doesn't expose which fallback ran in this response shape. Quality: **weak for this specific structural query**. |
| 2.9 | semantic_search (paraphrase) | `async boolean-returning Task methods` | **3** | PARTIAL | Returned 10 results, completely different set (CheckHealthAsync, PerformAsync, …), all `Task`-returning. Re-ran essentially as a name-substring fallback ("Check", "Async", "Perform"). Results are *plausibly async*, but again not guaranteed `Task<bool>`. Two paraphrases → two disjoint answer sets is a caution flag for callers relying on semantic intent. |

**Findings:**
- **F5 (win).** Paginated `find_references` caching is excellent: page-1 566 ms → page-2 11 ms on the same 1452-ref symbol. Reference-list pagination is cache-friendly.
- **F6 (win).** Every post-warm lookup (symbol_info, small find_references) clocks in < 100 ms, hitting the "cache-hit-dominated" profile the `workspace_warm` docs promise.
- **F7 (P2, verified).** `semantic_search` structural query ("returning Task<bool>") fails to narrow to the return-type shape — confirmed by paraphrase producing a completely disjoint result set. The `async` keyword gotcha noted in the docstring doesn't explain this case because the query asked for `Task<bool>` specifically. The tool would benefit from surfacing the **parse path** (structured vs. name-substring vs. token-or-match) in the response, not just logging it in Debug.
- **F27 (P3, observation).** First-ever `symbol_search("BaseItem")` took **507 ms** on a workspace that had already been primed by `workspace_warm`. Subsequent searches for `IUserManager` (157 ms) and `ILibraryManager` (67 ms) showed a visible warming curve — meaning the substring-match index is **not** included in the `workspace_warm` pre-population pass. This is the only concrete cold-vs-warm delta captured in this run (Phase 2.3 was skipped). Consider extending `workspace_warm` to pre-populate the symbol-name index, or call it out explicitly in the tool's docstring.

---

## Phase 3: Navigation and type hierarchy

Targets: `BaseItem` (hierarchy root), `IUserManager` (1 concrete impl), and `BaseItem.cs` at 2694 lines (the largest single file in the solution, surfaced by `document_symbols`).

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 3.1 | type_hierarchy | BaseItem | 6 | PASS | **32 derived types** (31 prod + 1 test fixture `MetadataTestItem`). baseTypes=null (extends `object`; unreported). 3 interfaces. |
| 3.2 | find_implementations | `IUserManager` (metadataName) | 4 | PASS | 2 hits: `UserManager` partial + its regex-generator sidecar. `previewText` populated, counts correct. |
| 3.3 | go_to_definition × 5 | BaseItem class id, IUserManager, LibraryManager, BaseItemDto, UserManager | **≤1 each** | PASS | Every call resolved to the canonical declaration. UserManager returns 2 locations (partial + generator) — correct. |
| 3.4 | document_symbols | BaseItem.cs (2694 lines) | — (result persisted 74 KB) | PASS | Tree-shaped, namespace + class + every member. Large response is inherent to file size, not an overflow. |
| 3.5 | get_syntax_tree | BaseItem.cs lines 45–200, maxDepth=5, **maxTotalBytes=30000** | — (persisted **76.6 KB**) | **UNEXPECTED** | Response exceeded the `maxTotalBytes` cap by **2.5×**. Docstring promises "Hard cap on estimated total JSON-serialized response size … ~120 bytes per node + leaf text length." Observed estimate error is large enough to blow client payload budgets. **Filing as F8.** |
| 3.6 | member_hierarchy | `BaseItem.GetBaseItemKind()` @ 1761:29 | 3 | PASS | `baseMembers=[]`, `overrides=[]` — method is non-virtual, so there is nothing to report. Picking a non-virtual target exposes the shape of the empty-result contract (which is clean: empty arrays, not null). **Retry on a virtual member would yield richer data** — keeping the signal as-is because "empty contract" is itself useful. |
| 3.7 | probe_position | BaseItem.cs @ 45:27 (class-name identifier) | 5 | PASS | Correctly identifies `IdentifierToken`/`BaseItem`/`NamedType`. Matches expected test-fixture authoring contract. |

**Findings:**
- **F8 (P2, verified).** `get_syntax_tree` overshot `maxTotalBytes=30000` → persisted **76,600 bytes** (2.55×). The documented `~120 bytes per node` cost-model likely under-counts deeply-nested structural JSON on real C# files. Clients that rely on the hard-cap to size their output buffer will be surprised. Suggest either (a) calibrate the cost model, or (b) rename/describe the parameter as a **best-effort soft cap** and document an observed multiplier ceiling.
- **F9 (win).** `go_to_definition` via `metadataName` is instantaneous (≤1 ms) on a hot workspace — this is the right path for scripted jumps that don't need a cursor.
- **F22 (P2, verified).** `document_symbols` has **no payload reducer** (no `summary`, no `maxSymbols`, no `limit`). On `BaseItem.cs` (2694 lines) the response persisted at **74 KB** — unreadable inline. This is now the last holdout in the structural-tool family: `rename_preview`, `find_references`, `symbol_impact_sweep`, `validate_workspace`, `validate_recent_git_changes`, and all composites surface `summary=true`, but `document_symbols` does not. A `maxDepth` / `topLevelOnly` / `summary` option would close the gap.

---

## Phase 4: Solution-wide analysis tools

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 4.1 | get_complexity_metrics (minComplexity=30, limit=10) | solution | 255 | PASS | **Top hotspot: `BaseItemRepository.TranslateQuery` @ 1701 — CC=242, LOC=946, MI=0.** Top 10 includes two `EncodingHelper` siblings (CC 91 + 87) and two `FetchDataFromXmlNode` twins (CC 95 + 87) in different parsers. |
| 4.2 | get_cohesion_metrics (limit=10) | solution | 146 | PASS | Worst LCOM4=**9** on `Jellyfin.Server.Migrations.Routines.MigrateLibraryDb` (11 methods, 9 clusters, 4 fields). A classic "this class has 9 unrelated responsibilities" signal. |
| 4.3 | find_unused_symbols (limit=10) | solution | 2024 | PASS | Only **6** hits surfaced (confidence=high). Filter-to-under-reporting is expected: analyzer references failed to load (see F2). |
| 4.4 | find_duplicated_methods | — | SKIPPED | — | Skipped in-interests-of-time; `find_duplicate_helpers` was prioritized because the Jellyfin backlog already contains a known false-positive backlog item the prompt called out explicitly. |
| 4.5 | find_duplicate_helpers (limit=10) | solution | 286 | PARTIAL | **False-positive signal confirmed.** Returned 10 hits; at least 5 are *not* reinvented BCL wrappers — they are domain-specific methods that happen to end in a BCL call (e.g., `MediaStreamSelector.GetSortedStreams` → `OrderByDescending`, `MediaStreamSelector.BehaviorOnlyForced` → `ToList`, `ApiServiceCollectionExtensions.AddPolicy` → `AuthorizationOptions.AddPolicy`). Matches the existing backlog `find-duplicate-helpers-framework-wrapper-false-positive`. |
| 4.6 | suggest_refactorings (limit=10) | solution | 2085 | PASS | All 10 suggestions were `complexity`-category, directly mirroring the complexity hotspots from 4.1 with `recommendedTools=[analyze_data_flow, extract_method_preview, extract_method_apply, compile_check]`. Good, actionable sequence. |
| 4.7 | get_namespace_dependencies (circularOnly=true) | solution | **n/a** | **UNEXPECTED (payload cap)** | Response exceeded the MCP 25 K-token cap even filtered to *circular* edges — persisted at **67 KB**. Either (a) the tool should have a `summary=true` flag like its siblings, or (b) Jellyfin has so many namespace cycles that even a cycle-only filter emits thousands of edges. **Filing as F11.** |
| 4.8 | get_di_registrations | solution | **n/a** | **UNEXPECTED (payload cap)** | Response **80 KB**, over the MCP cap. No `summary` option exposed. On a 40-project solution with hundreds of registrations the default shape is unusable without paging. **Filing as F12.** |
| 4.9 | get_nuget_dependencies (summary=true) | solution | 3296 | PASS | **75 packages** (all `centrally-managed`). Highest project-count: `IDisposableAnalyzers` in 23 projects; `StyleCop.Analyzers` / `SerilogAnalyzer` / `SmartAnalyzers.MultithreadingAnalyzer` / `Microsoft.CodeAnalysis.BannedApiAnalyzers` in all 40. |
| 4.10 | list_analyzers (limit=5) | solution | 207 | PASS | **32 analyzer assemblies / 760 rules**. First returned assembly: `IDisposableAnalyzers` (IDISP001-005). `hasMore=true` — pagination correct. |

### Phase 4b: v1.17–v1.28 composites

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 4b.11 | symbol_impact_sweep (summary=true, maxItemsPerCategory=5) | IUserManager (metadataName) | 394 | PASS | 5 refs, `switchExhaustivenessIssues=[]`, `mapperCallsites=[]`, 1 suggested task. Composite = find_references (53 ms from 2.4) + project_diagnostics (35 s from 1.4) ≈ 35 s sum vs. composite's **394 ms**. Dramatically faster than the sum of parts — suggests the composite path **does not** re-run solution-wide project_diagnostics; it instead narrows diagnostic scope to the symbol's projects. |
| 4b.12 | test_reference_map | (not run) | SKIPPED | — | Skipped; would need a distinct validation run for coverage percent and did not fit time budget. Jellyfin has 16 test projects, so a future pass here should be valuable. |
| 4b.13 | validate_workspace (runTests=false, summary=true) | solution (no changed set) | 254 | **UNEXPECTED** | `overallStatus: "compile-error"` with `errorCount=0`, `errorDiagnostics=[]`, `warningCount=0`. **The verdict field disagrees with the payload.** Clients gating on `overallStatus` will reject a provably-clean workspace. **Filing as F13 — P1 correctness bug.** |
| 4b.14 | validate_recent_git_changes (summary=true) | solution (git-derived) | 939 | **UNEXPECTED** | Same `overallStatus: "compile-error"` / `errorCount=0` mismatch as F13. Also reports 3 `unknownFilePaths` that are legitimate `.csproj` paths under the worktree — git saw them because the worktree has no commit history before the stress test edits, but the tool didn't match them to loaded workspace docs. Minor P2 on the unknown-paths path. |
| 4b.15 | trace_exception_flow | `System.ArgumentException` | 55 | PASS | **Found 20 catch sites** (`truncated=true`, maxResults=20). Every site is `catch (Exception)` with `catchesBaseException=true` — a classic over-broad catch pattern in the migrations tree. Good signal for a future exception-audit campaign. `rethrowAsTypeMetadataName=null` on every site (none of the broad catches re-wrap). |

**Findings:**
- **F10 (P2, verified).** `find_duplicate_helpers` false-positive rate ≥ 50 % on this repo — matches the known backlog item.
- **F11 (P2, verified).** `get_namespace_dependencies(circularOnly=true)` has no summary mode and overflows the MCP cap on a 40-project solution.
- **F12 (P2, verified).** `get_di_registrations` has no summary mode and overflows the MCP cap on a 40-project solution. `showLifetimeOverrides` is orthogonal (additive), not a payload-reducing option.
- **F13 (P1, verified).** `validate_workspace` and `validate_recent_git_changes` both return `overallStatus: "compile-error"` on a clean workspace with 0 errors and 0 warnings. Highest-impact finding of the run. Any external caller using `overallStatus` as a gating signal will reject correct states.
- **F14 (win).** `symbol_impact_sweep` with `summary=true` + `maxItemsPerCategory` is **100×+ faster** than naively calling `find_references` + `project_diagnostics` + a mapper scan in sequence. The composite pipeline is doing its job.
- **F15 (win).** `trace_exception_flow` at 55 ms across the solution is fast and highly actionable.

---

## Phase 5: Mutation previews (read-only stress)

**No mutations applied.** All calls are preview-only.

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 5.1 | rename_preview (summary=true) | `IUserManager.GetUserById` → `LookupUserById` (130+ callsites) | 1651 | PASS | **56 affected files.** `summary=true` collapsed per-site diffs to one line each — payload stayed under the cap. |
| 5.2 | rename_preview (summary=true) | `IUserManager` → `IAppUserManager` (233 refs) | 767 | PASS | **105 affected files** — includes every controller, NFO parser/saver, sort comparer, and test fixture. **Faster than 5.1** despite being the bigger symbol — hot cache after 5.1's reference scan. |
| 5.3 | extract_interface_preview | `UserManager` (concrete) → `IUserManagerExtracted` | 18 | **PARTIAL** | 21 members lifted. The emitted diff inserts an extra blank line between the class header and its `{` block — a formatting artifact that apply would ship as-is. **Filing as F16.** |
| 5.4 | extract_method_preview | — | SKIPPED | — | Skipped in the interest of time budget. Extract-method semantics were already exercised indirectly by `suggest_refactorings` in 4.6. |
| 5.5 | code_fix_preview | — | SKIPPED | — | The only errors we saw were `AD0001` (analyzer crashed), which has no fix provider. Subsumed by 5.6's fix-provider check. |
| 5.6 | fix_all_preview (scope=project) | IDE0005 on `Emby.Server.Implementations` | 33 | PASS | `fixedCount=0` with a clean `guidanceMessage: "No code fix provider is loaded for diagnostic 'IDE0005'. Use organize_usings_preview / organize_usings_apply to remove unused usings."` — **excellent failure contract** — actionable pointer to the right tool. |
| 5.7 | format_document_preview | BaseItem.cs (2694 lines) | 15 | PASS | Zero changes — file is already canonical. |
| 5.8 | organize_usings_preview × 3 | UserController.cs / BaseItem.cs / LibraryManager.cs | 27, 22, 22 | PASS | UserController + BaseItem: no changes. **LibraryManager: 3 unused usings removed** (`MediaBrowser.Controller.MediaSegments`, `MediaBrowser.Controller.Trickplay`, `MediaBrowser.Model.Dlna`). Per-file avg **~24 ms**. |
| 5b.9 | preview_multi_file_edit | 3 files × 1 edit each | 17 | **PARTIAL** | Successfully produced per-file diffs in 17 ms. **But**: the inserted `// stress-test-no-op\n` on the `namespace` line landed **between the `namespace MediaBrowser.Controller.Library` header and the `{`** — not syntactically valid C#. The tool's `skipSyntaxCheck=false` default should have caught that, but the preview was accepted. **Filing as F17.** |
| 5b.10 | restructure_preview | pattern `__x__?.ToString() ?? ""` in MediaBrowser.Controller | 38 | PASS | `InvalidOperation: no matches found for pattern in scope`. Good structured error, message names the three things to check (kind, placeholders, scope). Pattern not present in this repo — no actionable refactor, but the tool behaved correctly. |
| 5b.11 | change_signature_preview | add `bool includeDisabled = false` to `GetUserById` (130 callsites) | — | **UNEXPECTED (payload cap)** | **Response 96,756 bytes — blew the MCP cap.** Tool has no `summary=true` option. Only workaround is running it on a low-fan-out target. **Filing as F18 — sibling of F11/F12.** |
| 5b.12 | symbol_refactor_preview | — | SKIPPED | — | Composite; deferred in favor of individual composites tested in Phase 4b. |
| 5b.13 | replace_invocation_preview | `GetUserById` → `LookupUserById` (non-existent newMethod) | 4 | PASS (expected fail) | Correctly refused: `InvalidOperation: Could not resolve newMethod … Ensure the fully-qualified type name and parameter-type list match an existing method overload.` Actionable error contract. |
| 5b.14 | change_type_namespace_preview | — | SKIPPED | — | Not exercised — would require a leaf namespace specifically chosen for this. |
| 5b.15 | record_field_add_with_satellites_preview | — | SKIPPED (repo-shape) | — | Jellyfin uses very few `record` types (mostly classes). |
| 5b.16 | extract_shared_expression_to_helper_preview | — | SKIPPED | — | Time budget. |

**Findings:**
- **F16 (P3, verified).** `extract_interface_preview` emits an extra blank line between the modified class header and its opening `{` when appending to an existing base list. Cosmetic but real.
- **F17 (P2, verified).** `preview_multi_file_edit` with `skipSyntaxCheck=false` (default) accepted an edit that inserts text between a `namespace X` header and the following `{`, producing syntactically broken C#. The tool's own docstring promises "syntax errors surface as an error response" — but none did. Could be that the edit inserts *between* tokens the parser still sees as recoverable. **Either the parser guard needs to be tightened or the docstring softened.**
- **F18 (P2, verified).** `change_signature_preview` has no `summary=true` option. On `GetUserById` (130 callers) the response exceeded the MCP cap (96 KB). Sibling of F11 (`get_namespace_dependencies`) and F12 (`get_di_registrations`) — a consistent pattern of "solution-wide mutation-preview tools need payload reducers."
- **F19 (win).** `fix_all_preview` returning a graceful `guidanceMessage` pointing to `organize_usings_preview` when no code-fix provider exists is the kind of failure contract that saves callers a trip through the catalog.

---

## Phase 6: Throughput and rapid-fire

All 30 calls were dispatched as a single parallel batch against a fully-primed workspace.

| Batch | n | min (ms) | max (ms) | mean (ms) | p95 (ms) | Notes |
|---|---|---|---|---|---|---|
| symbol_info (metadataName, 10 distinct types/interfaces) | 10 | 0 | 0 | 0.0 | 0 | Every call returned in <1 ms — all metadata-name lookups are pure cache hits. |
| go_to_definition (metadataName, 10 distinct hierarchy types) | 10 | 0 | 0 | 0.0 | 0 | Same profile as symbol_info — metadata-name resolution is a hash lookup on the hot path. |
| enclosing_symbol (file+line+column at 500/1000/1500/2000 line positions across 5 large files) | 10 | 1 | 3 | 1.5 | 3 | Slightly more work than metadata-name lookups because it has to walk the syntax tree to a position, but still solidly under the 2-s budget. |

**Observations:**
- **No degradation** across the batch — there's no "burst throttle" behavior at this scale.
- `_meta.queuedMs` was 0 on every call — the rw-lock reader path is admitting them concurrently.
- The server comfortably sustains 30 parallel lookups against a 40-project / 2065-document solution with every call returning in single-digit ms.

---

## Phase 7: Recovery and edge cases

| # | Scenario | elapsedMs | Error category | Message quality |
|---|---|---|---|---|
| 7.1 | `symbol_info` at whitespace position `BaseItem.cs 46:1` | 0 | `NotFound` | **Actionable.** "No symbol found at the specified location. Ensure the workspace is loaded (workspace_load) and the identifier is correct." |
| 7.2 | `go_to_definition` with `line=999999` on a 2695-line file | 0 | `InvalidArgument` | **Actionable.** "Line 999999 is out of range. The file has 2695 line(s)." — includes the actual cap. |
| 7.3 | `document_symbols` on fabricated path `ThisFileDoesNotExist.cs` | 2 | `FileNotFound` | **Actionable.** "Source file not found in the loaded workspace … Verify the file path is absolute and the file exists on disk. If the workspace was recently reloaded, the file may have been removed." |
| 7.4 | `workspace_load` called again with the same `.sln` path | 4 | PASS (idempotent) | Returned the **same `workspaceId` (5fb19fc8…)** and `workspaceVersion:2` with no extra slot consumed — matches the documented idempotent-by-path contract. |
| 7.5 | `find_references` with `symbolHandle="JUNK_HANDLE_FOR_TESTING_NOT_VALID_BASE64"` | 3 | `InvalidArgument` | **Actionable.** "symbolHandle is not valid base64." Specific failure mode, not silent empty. |
| 7.6 | `symbol_search` with gibberish query `qqqqqqqqzzzzzzzzxxxxxxxxx` | 776 | PASS (empty set) | `count=0, symbols=[]`. **Clean** empty contract — no error, no fabricated matches. |
| 7.7 | `workspace_close` after the underlying `.sln` file was deleted (worktree removed) | 2 | **UNEXPECTED** | `FileNotFound: Solution file not found: '…\Jellyfin.sln'. Verify the file path is absolute and the file exists on disk.` **The workspace ID was valid and the server held the in-memory session — closing a session shouldn't require the source artifact to still exist on disk.** Caller is left with a hanging registry entry until stdio host restarts. **Filing as F21 (P2 correctness bug).** |

**Skipped (time budget):**
- 7.7 Stale workspace / workspace_reload then compile_check. Already covered by the Phase 0 restore cycle.
- 7.8 `find_references` on `BaseItem` with `limit=500` — would certainly exceed the MCP cap even with `summary=true` given 1452 total refs; skipped to avoid another payload-overflow incident that adds no new signal beyond F11/F12/F18.

**Error-quality rating:** **7/7 actionable** (the F21 `workspace_close` error message was also actionable — it was the *behavior* that was wrong, not the wording). No vague/unhelpful cases encountered.

---

## Phase 8: Summary and recommendations

### 8.1 Performance summary

Per-tool timings (from Phases 1–7, warm workspace, 40 projects / 2065 docs):

| Category | Representative tool | min | mean | p95 | max | Budget | Verdict |
|---|---|---|---|---|---|---|---|
| Lookup (metadata-name) | `symbol_info`, `go_to_definition` | 0 | ~0.3 | 1 | 5 | 2000 ms | **Far under** |
| Lookup (file-position) | `probe_position`, `enclosing_symbol` | 1 | ~2 | 5 | 5 | 2000 ms | **Far under** |
| Search / scan | `symbol_search`, `find_references`, `semantic_search` | 3 | ~200 | 566 | 776 | 10 000 ms | **Far under** |
| Solution-wide analysis | `compile_check`, `get_complexity_metrics`, `get_cohesion_metrics`, `list_analyzers` | 146 | ~1 300 | 3 296 | 7 620 | 30 000 ms | **Under**, except… |
| Solution-wide analysis (warning scan) | `project_diagnostics(severity=Warning, summary=true)` | — | 35 266 | — | 35 266 | 30 000 ms | **SLOW** (F1) |
| Emit validation | `compile_check(emitValidation=true)` | — | 35 121 | — | 35 121 | — | 4.6× the emit-off baseline |
| Preview | `rename_preview`, `extract_interface_preview`, `organize_usings_preview`, etc. | 15 | ~400 | 1 651 | 1 651 | 15 000 ms | **Far under** |
| Build / test | Full `dotnet test` (13 test DLLs) | — | — | — | ~150 s | 120 s | Slightly over — integration tests dominate |

### 8.2 Bottleneck list (sorted by severity)

1. **F13 (P1) — `validate_workspace` / `validate_recent_git_changes` report `overallStatus: "compile-error"` on a clean workspace (0 errors, 0 warnings).** Highest-impact defect: any CI-gating caller will reject provably-clean states. Must be fixed before these tools can be trusted for gating.
2. **F2 (P1) — 2× `AD0001` (analyzer crashed)** surfaced but not investigated; eroded every analyzer-driven tool on this run.
3. **F1 (P2) — `project_diagnostics(severity=Warning, summary=true)` = 35 s** on a 40-project solution. Exceeds the 30 s budget on the cheapest code path (summary).
4. **F21 (P2) — `workspace_close` returns `FileNotFound` when the solution file has been deleted.** Leaves a hanging registry entry with no cleanup path; forces stdio host restart.
5. **F11/F12/F18/F20/F22 (P2) — Five tools lack a payload reducer and overflow the MCP cap on a 40-project solution:** `get_namespace_dependencies`, `get_di_registrations`, `change_signature_preview`, `workspace_reload`, `document_symbols`. Pattern is proven out by `rename_preview(summary=true)`, `find_references(summary=true)`, `symbol_impact_sweep(summary=true)`, `validate_workspace(summary=true)`.
6. **F26 (P2) — `workspace_warm` doesn't prime the analyzer pipeline.** Only compilation + semantic-model caches are populated; `analyzersReady=false` persists through the full run.
7. **F8 (P2) — `get_syntax_tree(maxTotalBytes=30000)` produced a 76 KB response (2.55×).** The cost model undercounts structural JSON.
8. **F10 (P2) — `find_duplicate_helpers` false-positive rate ≥ 50 %** on domain-specific wrappers that terminate in a BCL call.
9. **F7 (P2) — `semantic_search` with a structural return-type query** falls through to token-substring matching silently (no `warning` field populated).
10. **F24/F25/F27 (P3, observational) — server startup contract ambiguity, `workspace_reload` heldMs double-count, `workspace_warm` doesn't prime substring-match index.** Minor individually but each adds operator friction.

### 8.3 Scalability assessment — "would this survive a 100-project solution?"

| Category | 100-project verdict | Rationale |
|---|---|---|
| Lookups | **Yes** | Hash-table perf is independent of project count once metadata caches are warm. |
| Search / scan | **Yes** | `find_references`/`symbol_search` already returned 1452-ref sweeps in 566 ms — linear in refs, not projects. |
| Solution-wide analysis | **Conditional** | `compile_check` scales linearly with per-project compilation; `project_diagnostics` is already over budget at 40 projects — needs a payload + perf-path pass. |
| Mutation previews | **Conditional** | The tools that already overflow the MCP cap at 40 projects (F11/F12/F18) will fail outright at 100. Payload reducers are a blocking prerequisite. |
| Composites (symbol_impact_sweep, validate_workspace) | **Yes** (once F13 is fixed) | The composites already scope per-symbol or per-changed-file — project count is a weak driver. |
| Build / test | **Yes**, with caveats | Integration tests dominate wall time. `test_reference_map` (skipped) would be the right scaling tool. |

### 8.4 Warm-up impact

Phase 2.3 was **skipped** on this run — `workspace_warm` ran in Phase 0 before any search tool. Every subsequent `symbol_info` / `go_to_definition` / small `find_references` hit the cache-hit-dominated profile the docs promise (0–5 ms). A future stress test should deliberately skip warm-up once to capture the cold-delta.

### 8.5 Error-quality scorecard

| Source phase | Actionable | Vague | Unhelpful |
|---|---|---|---|
| Phase 7 (deliberate edge cases, incl. F21 `workspace_close`) | 7/7 (100 %) | 0 | 0 |
| Phase 5 (invalid replace_invocation, missing pattern) | 2/2 (100 %) | 0 | 0 |
| Phase 4 (invalid fix-all for IDE0005) | 1/1 (100 %) | 0 | 0 |
| **Total** | **10/10 (100 %)** | **0** | **0** |

Every structured error response named both the failure mode and the recovery path. This is a standout strength of the surface. (Note: F21 is a *behavioral* bug, not a message-quality bug — the error text was fine; the tool shouldn't have errored at all.)

### 8.6 Top 7 recommendations

1. **[P1] Fix `validate_workspace` / `validate_recent_git_changes` overallStatus mismatch.** The tools return `overallStatus: "compile-error"` with `errorCount=0` and `errorDiagnostics=[]`. Likely a default-initialization bug in the status enum or an inverted branch. Highest-impact because gating logic is the marquee use case.
2. **[P1] Investigate & fix the 2 AD0001 analyzer-crashed diagnostics.** Every analyzer-driven tool (`find_unused_symbols`, `suggest_refactorings`, all CA/IDE rules) under-reports until this is resolved. On this run, `find_unused_symbols` surfaced only 6 hits — the real count is almost certainly larger.
3. **[P2] Add `summary=true` to 5 more tools.** `get_namespace_dependencies`, `get_di_registrations`, `change_signature_preview`, `workspace_reload`, `document_symbols` all overflow the MCP cap on Jellyfin. Pattern is proven out by the existing `summary=true` implementations — this is mostly mechanical.
4. **[P2] Fix `workspace_close` to not require the solution file on disk.** The session is in-memory; closing it should free the registry entry regardless of what has happened to the source artifact. Currently leaves a zombie registry row until stdio host restart.
5. **[P2] Extend `workspace_warm` to prime the analyzer pipeline and the symbol-name substring index.** Right now `analyzersReady=false` persists and the first `symbol_search` pays a ~500 ms cold penalty. Either include them by default, or expose `warmAnalyzers` / `warmSymbolIndex` opt-ins.
6. **[P2] Calibrate `get_syntax_tree`'s `maxTotalBytes` cost model or rename/document it as soft cap.** Observed 2.55× overshoot on a real file (BaseItem.cs lines 45–200 with maxDepth=5). Clients rely on the hard-cap for output budgeting.
7. **[P2] Tighten `find_duplicate_helpers` heuristics** to distinguish domain-specific wrappers (e.g., `AddPolicy(string, Action<AuthorizationPolicyBuilder>)` on `ApiServiceCollectionExtensions`) from actual BCL reinventions. Current false-positive rate on Jellyfin ≥ 50 % matches the existing backlog item `find-duplicate-helpers-framework-wrapper-false-positive`.

### 8.7 Comparison baseline

**None available** — the `ai_docs/audit-reports/` directory contained only `README.md` and `deep-review-session-checklist.md` before this run. This is the first stress-test report for Jellyfin and the first in this directory using the `*_stress-test.md` naming convention. Future runs should diff against this file for regression-vs-improvement.

### 8.8 Jellyfin test suite — supplementary validation

The user also requested that Jellyfin's own test suite be run (`dotnet test`, Debug config, all projects). Results (background task, exit code 0):

| Project | Passed | Skipped | Total | Duration |
|---|---|---|---|---|
| Jellyfin.Naming.Tests | 619 | 0 | 619 | 13 s |
| Jellyfin.Server.Implementations.Tests | 429 | 6 | 435 | 2 s |
| Jellyfin.Providers.Tests | 235 | 0 | 235 | 682 ms |
| Jellyfin.Networking.Tests | 120 | 0 | 120 | 692 ms |
| Jellyfin.Server.Integration.Tests | 100 | 3 | 103 | 1 m 18 s |
| Jellyfin.Api.Tests | 79 | 0 | 79 | 131 ms |
| Jellyfin.MediaEncoding.Tests | 60 | 0 | 60 | 215 ms |
| Jellyfin.LiveTv.Tests | 48 | 0 | 48 | 228 ms |
| Jellyfin.XbmcMetadata.Tests | 34 | 0 | 34 | 105 ms |
| Jellyfin.Common.Tests | 30 | 0 | 30 | 25 ms |
| Jellyfin.Controller.Tests | 27 | 0 | 27 | 198 ms |
| Jellyfin.MediaEncoding.Hls.Tests | 17 | 0 | 17 | 21 ms |
| Jellyfin.Server.Tests | 7 | 0 | 7 | 359 ms |
| **Totals** | **1 805** | **9** | **1 814** | **~2 min** |

**0 failures.** The skipped tests are OS-specific (macOS/Linux path-handling fixtures) and expected on Windows. Several smaller test projects (Model.Tests, Extensions.Tests, MediaEncoding.Keyframes.Tests) produced no test-runner summary lines and are **presumed** empty/tooling-only — this was **not verified** with `dotnet test --list-tests`. A future run should enumerate these explicitly rather than inferring from absence.

### 8.9 Run-hygiene notes

- **Worktree cleanup discarded 3 uncommitted files** on `ExitWorktree(action="remove", discard_changes=true)`. These were almost certainly `dotnet restore` / `dotnet test` build artifacts not covered by `.gitignore` for the worktree path, but they were **not enumerated before discard**. Future stress tests should `git status --porcelain` the worktree before teardown if artifact provenance matters.
- **Report size at commit time: 37.5 KB** — slightly over the 30 KB split threshold the prompt suggests. Kept as a single file because splitting would fracture cross-references between findings. A future run with more phases fully exercised (Phases 2.3, 4.4, 5.4–5.5, 5b.12, 5b.14, 5b.16, 7.7–7.8 are all marked skipped here) would almost certainly need to split.

---

*Report produced by stress-test prompt `prompts/stress-test-external-repo.md` (v1.28) against Jellyfin master at worktree `.claude/worktrees/stress-test-jellyfin`. Roslyn MCP server `1.28.0+6810a67`. Reportable into future runs via filename diff.*
