# Top 10 Remediation Plan — 2026-04-14

**Status:** Awaiting human approval. No code written.

**Source:** `ai_docs/backlog.md` Open work table.

**Selection criteria:**
1. Code correctness risk first.
2. Blast radius (how many tools/workflows broken).
3. Single-PR feasibility.

**Constraints applied:** Breaking changes to schemas/parameter names allowed (no backcompat). Boy-scout perf only on touched code. All claims verified against source at `src/RoslynMcp.{Roslyn,Host.Stdio,Core}/`.

**Jellyfin baseline (40 projects, from `20260414T120000Z_jellyfin_stress-test.md` Phase 8):**
- Lookup p95: 2517 ms (budget ≤2 s)
- Search p95: 4864 ms (budget ≤10 s)
- Analysis p95: 35553 ms (budget ≤30 s) — `project_diagnostics` exceeds
- Mutation preview p95: 5512 ms (budget ≤15 s)

---

## Independent verification summary

| id | Backlog claim | Verification | Confidence |
|----|---------------|--------------|------------|
| `unresolved-analyzer-reference-crash` | 6 tools crash on `UnresolvedAnalyzerReference` | Confirmed — guards exist only in `CompilationCache.BuildCompilationWithAnalyzersAsync` (line 105–108) and `FixAllService.CollectProjectAnalyzersForDiagnosticId` (line 436). Other paths (`SymbolFinder.FindReferencesAsync`, `Compilation.GetDiagnostics`, `SymbolFinder.FindDerivedClassesAsync`) reach project state that still includes the unresolved entries. Audit phases 2/3 of stress-test reconfirmed 10 errors from this one root cause as recently as 2026-04-14. | High |
| `extract-method-apply-var-redeclaration` | Generates `var x = M(...);` redeclaring an existing local | Confirmed at `ExtractMethodService.cs:228-237`: when `flowsOut.Count == 1`, the call site is unconditionally a `LocalDeclarationStatement` with `var`. No check against `dataFlow.VariablesDeclared` (which lists locals **declared inside** the region) vs locals declared before. | High |
| `get-source-text-line-range-ignored` | `startLine`/`endLine` ignored | Verified at `WorkspaceTools.cs:164-179`: tool signature has **no** `startLine`/`endLine` parameters at all. Agents passed them and the MCP framework dropped unknown args silently. | High |
| `format-range-preview-nonfunctional` | Returns "schema/server error" on every input | Code path at `RefactoringService.cs:222-243` looks correct in isolation. No tests exist (zero hits in `tests/`). Likely: out-of-bounds `startColumn-1` producing negative `startPosition` for column 1 inputs, or `Formatter.FormatAsync(document, span, …)` overload binding mismatch under .NET 10 Roslyn 5.x. Needs runtime repro. | Medium — bug exists, root cause unconfirmed |
| `goto-type-definition-local-vars` | Returns NotFound for local variables | Partial. `SymbolNavigationService.cs:42-79`: switch handles `ILocalSymbol → local.Type as INamedTypeSymbol`, but: (1) only `INamedTypeSymbol` is unwrapped — fails for `IArrayTypeSymbol`, `ITypeParameterSymbol`, `IPointerTypeSymbol`; (2) even when the type is `INamedTypeSymbol` (e.g. `IEnumerable<UserDto>`), `typeSymbol.Locations.Where(l => l.IsInSource)` is empty for metadata types like `IEnumerable<T>` — the function returns `[]` because the `SpecialType.None` guard at line 63 only catches primitives. So a local of type `IEnumerable<UserDto>` finds nothing. | High |
| `diagnostics-resource-timeout` | Resource hangs >30 s while the equivalent tool succeeds | Confirmed at `WorkspaceResources.cs:71-79`: the resource calls `IDiagnosticService.GetDiagnosticsAsync(workspaceId, null, null, null, null, ct)` with no pagination, then serializes the full result. The `project_diagnostics` tool layers pagination at the tool layer (`AnalysisTools.cs:53-56`) — the resource bypasses that. On Jellyfin (3433 diagnostics) this serializes ~2 MB JSON. | High |
| `project-diagnostics-large-solution-perf` | 29–35 s on Jellyfin exceeds 30 s budget | Confirmed at `DiagnosticService.cs:35-105`: parallelizes per-project but does both `compilation.GetDiagnostics(ct)` AND `compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct)` for every project unconditionally, even when `severityFilter=Warning` would let us skip Info-only analyzer rules. Also re-runs analyzers on every call (no per-`workspaceVersion` cache for the result, only for raw Diagnostic objects via `_diagnosticCache` for the detail-lookup path). | High |
| `code-fix-providers-missing-ca` | "No curated code fix" for all CA*/IDE* rules | Confirmed at `RefactoringService.PreviewCodeFixAsync` (line 245-302): hardcoded to **only** support `CS8019 / remove_unused_using`. Throws for everything else. The plumbing for loading providers from analyzer assemblies already exists in `FixAllService.LoadCodeFixProvidersFromAnalyzerReferences` (line 389-427) but `code_fix_preview` doesn't use it. | High |
| `apply-project-mutation-whitespace` | Blank line after `<PackageReference>` removal | Confirmed at `ProjectMutationService.cs:82-95`: simple `element.Remove()` on `XElement` loaded with `LoadOptions.PreserveWhitespace` — the surrounding text node (whitespace + newline) is preserved, leaving a blank line. | High |
| `analyze-data-flow-inverted-range` | `startLine > endLine` returns wrong error code | Confirmed at `FlowAnalysisService.cs:20-57` and `cs:59-142`: no upfront validation; inverted range falls through to "No statements found in the line range 200-100" because the predicate `>= startLine0 && <= endLine0` is never true. Same bug in `AnalyzeControlFlowAsync`. | High |

**Items skipped from top picks:**

- `cohesion-metrics-null-lcom4` (P3) — Cannot reproduce. `Lcom4Score` is non-nullable `int`, always set to `clusters.Count` (≥1) for classes or `methodCount` for interfaces. Existing tests assert `Lcom4Score >= 1`. The audit's "null" claim is most likely a JSON-field-name confusion (camelCase `lcom4Score` vs expected `LCOM4Score`). Dropping from this batch; recommend marking the backlog row as needing fresh repro before further work.
- `concurrent-mcp-instances-no-tools` (P3) — Investigation required, not fixable in one PR; root cause unknown (stdio handle contention, plugin registration race, or other).
- `mcp-connection-session-resilience` (P3) — Cross-cutting docs/UX item; depends on host behavior, not a code fix.
- `schema-drift-jellyfin-audit` (P3) — Bundled multi-tool drift items; should be split per row before tackling.

---

## Implementation plan

Each item is intended as **one focused PR**. Items are independent unless noted under "Backlog sync". Order is suggestive — items with the largest blast radius first.

### 1. `unresolved-analyzer-reference-crash` (P2)

| Field | Content |
|-------|---------|
| **Diagnosis** | `Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference` is the placeholder Roslyn uses when a project's `<Analyzer Include="…"/>` cannot be resolved at workspace-load time (typical for `netstandard2.0` analyzer projects whose output paths are not on disk yet, or for analyzer projects referenced by GAC path). The CompilationCache and FixAllService already filter it (`Where(r => r is not UnresolvedAnalyzerReference)`), but downstream callers — `SymbolFinder.FindReferencesAsync`, `SymbolFinder.FindDerivedClassesAsync`, `compilation.GetDiagnostics`, etc. — internally re-enumerate `Project.AnalyzerReferences`, hit an unhandled subtype in a Roslyn `switch`, and throw `InvalidOperationException("Unexpected value 'Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference'")`. The 6 tools listed in the backlog (`find_unused_symbols`, `type_hierarchy`, `find_implementations`, `member_hierarchy`, `impact_analysis`, `suggest_refactorings`) all share this pattern. Stress-test phases 2/3 reconfirmed the crash on 2026-04-14. |
| **Approach** | Strip unresolved references **at workspace load** so no downstream caller — ours or Roslyn's — can ever see them. In `WorkspaceManager.LoadIntoSessionAsync` (`src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:474-542`), after `OpenSolutionAsync` / `OpenProjectAsync` completes: walk `session.Workspace.CurrentSolution.Projects`, for each project collect `project.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>()`, and call `Workspace.OnAnalyzerReferenceRemoved(projectId, reference)` for each one. Emit a `WORKSPACE_UNRESOLVED_ANALYZER` warning (severity Warning, not Error) per stripped reference into `session.WorkspaceDiagnostics` so callers can still discover that something was filtered. The `WorkspaceFailed` handler already feeds `session.WorkspaceDiagnostics`; reuse the `WorkspaceDiagnosticSeverityClassifier` at `src/RoslynMcp.Roslyn/Helpers/WorkspaceDiagnosticSeverityClassifier.cs` so the new code path uses the same classifier. After the strip, also delete the now-redundant `Where(r => r is not UnresolvedAnalyzerReference)` guards in `CompilationCache.cs:105-108` and `FixAllService.cs:435-436` (with the FLAG-A comments) — single source of truth at load time. |
| **Scope** | Modified: `WorkspaceManager.cs` (~30 LOC added in `LoadIntoSessionAsync`), `CompilationCache.cs` (delete 4 LOC + comment), `FixAllService.cs` (delete 4 LOC + comment). New test file: `tests/RoslynMcp.Tests/UnresolvedAnalyzerReferenceTests.cs` with a fixture project that ships an unresolved analyzer ref (synthesize via direct `Workspace.OnAnalyzerReferenceAdded(projectId, new UnresolvedAnalyzerReference(...))` after load — Roslyn exposes the type publicly). New files: 1. Modified: 3. |
| **Risks** | (a) `OnAnalyzerReferenceRemoved` may invalidate any cached `Compilation` for the affected project — `CompilationCache` keys on `workspaceVersion` and we bump it on `LoadIntoSessionAsync` (line 533 already does `IncrementVersion`), so cache entries written before the strip will be discarded on next read. Verify with the cache's existing concurrent-update tests. (b) Some downstream callers may have observed the unresolved reference and stored it; since we run before any tool can be invoked (load → first tool call), there is no in-flight reader. (c) If `OnAnalyzerReferenceRemoved` itself fails for an exotic subclass we have not seen, swallow + log to `session.WorkspaceDiagnostics` rather than crashing the load. |
| **Validation** | New tests: (i) load a synthetic solution with `UnresolvedAnalyzerReference` injected, assert `find_unused_symbols`, `type_hierarchy`, `find_implementations`, `member_hierarchy`, `impact_analysis`, `suggest_refactorings` all return successfully (not throw `InvalidOperationException`); (ii) assert `workspace_status` reports the `WORKSPACE_UNRESOLVED_ANALYZER` warning so the strip is visible to operators; (iii) assert no duplicate strip after `workspace_reload`. Manual: re-run Jellyfin stress-test phases 2/3, expect zero `UnresolvedAnalyzerReference` errors. |
| **Performance review** | `find_unused_symbols` was *blocked* (cannot complete) on Jellyfin, so the perf delta is "from `ERROR` to a real number." After the fix: expect `find_unused_symbols` ~5–15 s on Jellyfin (in line with other advanced-analysis tools). `type_hierarchy` Phase 3 was ~28 ms when it errored fast; after the fix ~50–200 ms (full traversal). The strip itself is O(projects × unresolved-refs-per-project), typically <10 entries × 40 projects = trivial (<10 ms) at load. |
| **Backlog sync** | Closes `unresolved-analyzer-reference-crash`. Update `compilation-prewarm-on-load` row's "Investigation" note: with this fix landed, agents can re-measure cold starts to determine whether the original cold-start spikes were real Roslyn lazy compilation or were a side-effect of the failed analyzer-reference path. |

---

### 2. `extract-method-apply-var-redeclaration` (P2)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:229-241`, when `flowsOut.Count == 1`, the call site is unconditionally synthesized as a `LocalDeclarationStatement` with `var x = …`. This is correct **only** if `flowsOut[0]` is a brand-new local introduced by the extracted region. When the variable already exists in the enclosing scope (the common case — re-assigning an existing local mid-method), the `var` declaration shadows it and the compiler emits CS0136 + CS0841. Reproduced on Jellyfin Phase 6j: extracting four lines from `BaseItemRepository.TranslateQuery` into `ApplyMinWidthFilter` produced `var baseQuery = ApplyMinWidthFilter(baseQuery, minWidth);` instead of `baseQuery = ApplyMinWidthFilter(baseQuery, minWidth);`. Roslyn already gives us the answer — `dataFlow.VariablesDeclared` lists symbols declared **inside** the region; if `flowsOut[0]` is **not** in that set, the variable exists in the outer scope and we must assign, not declare. |
| **Approach** | Modify `AnalyzeFlowAndInferSignature` (`ExtractMethodService.cs:121-175`) to also return `dataFlow.VariablesDeclared` as a `HashSet<string>` (or pass through the `ImmutableArray<ISymbol>` and compare by symbol). Modify `BuildMethodAndCallSite` (line 177-248) to take that set: when `flowsOut[0].Name` is **not** in `variablesDeclaredInside`, build an `ExpressionStatement` wrapping an `AssignmentExpression` (`flowsOut[0].Name = M(...)`) instead of a `LocalDeclarationStatement`. When it **is** in the set (variable was first declared inside the region), keep the current `var x = M(...)` shape. |
| **Scope** | Modified: `ExtractMethodService.cs` (~30 LOC, contained to two private methods). Tests: extend `tests/RoslynMcp.Tests/ExtractMethodTests.cs` with two new cases — (a) extract that flows out a pre-existing local, expect plain assignment; (b) extract that flows out a region-local, expect `var` declaration. New files: 0. Modified: 2. |
| **Risks** | (a) Compound assignments (e.g. `x +=`) — the extracted region may write to the local via a compound operator. Roslyn's `DataFlowsOut` reports the symbol regardless of operator; the assignment we synthesize uses `=` which discards the read-modify-write semantics. Mitigation: detect by inspecting `dataFlow.AlwaysAssigned` — if the variable is in `DataFlowsOut` but not `AlwaysAssigned`, conservatively reject the extraction with a clear error rather than emit incorrect code. (b) Type inference: if the original local was `var x = …` and we replace with `x = …`, the type stays the original inferred type — no change. (c) Captured locals (closures): out-of-scope; the existing 1-flowsOut-max guard already covers most cases. |
| **Validation** | Tests in `ExtractMethodTests.cs`. Manual: re-run the Jellyfin Phase 6j extraction and confirm `compile_check` returns 0 errors after `extract_method_apply`. Compile of the regression fixture must succeed (no CS0136/CS0841). |
| **Performance review** | N/A — correctness fix, no hot-path changes. The added work is one `HashSet.Contains` per `flowsOut` symbol, microseconds per call. |
| **Backlog sync** | Closes `extract-method-apply-var-redeclaration`. Reduces urgency of `apply-with-verify-and-rollback` (which currently lists this bug as a `deps`); the workflow gap remains valid but the most painful trigger is gone. |

---

### 3. `get-source-text-line-range-ignored` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:164-179`, the `get_source_text` tool has only `workspaceId` and `filePath` parameters. The MCP framework discards unknown parameters silently, so when stress-test agents passed `startLine`/`endLine` (from intuition, since `roslyn://workspace/{id}/file/{path}` resource is similarly path-only) they got the full file every time. On `EncodingHelper.cs` (384 KB, 7890 lines) that is ~100× the agent's intent and trips MCP context budgets after a few reads. |
| **Approach** | Add three optional parameters to `GetSourceText`: `[Description("Optional: 1-based first line to return (inclusive)")] int? startLine = null`, `[Description("Optional: 1-based last line to return (inclusive)")] int? endLine = null`, `[Description("Maximum characters to return (default 65536). Truncates with a marker if the requested range exceeds the cap.")] int maxChars = 65536`. Slice the loaded `SourceText` server-side via `text.Lines[startLine-1..endLine]` (offset arithmetic identical to `RefactoringService.PreviewFormatRangeAsync` for span building). Return the JSON envelope augmented with `requestedStartLine`, `requestedEndLine`, `returnedStartLine`, `returnedEndLine`, `totalLineCount`, `truncated` so callers can detect both range honoring and length capping. Validate `startLine >= 1`, `endLine >= startLine`, `endLine <= totalLineCount` with structured errors. Update the tool description to document the new parameters and the 65 KB cap. |
| **Scope** | Modified: `WorkspaceTools.cs` (~40 LOC). New tests in `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`: full-file (no params), valid range, range past EOF (clamp + marker), `endLine < startLine` (structured error), oversize range (truncated marker). New files: 0. Modified: 2. |
| **Risks** | (a) Line endings: `SourceText.Lines[i].End` is the line-terminator-exclusive end; we need `text.Lines[endLine-1].EndIncludingLineBreak` to keep the trailing newline. Tested via fixtures with both LF and CRLF. (b) Resource symmetry: the `roslyn://workspace/{id}/file/{path}` resource currently returns the whole file (line 84-109). Out of scope for this PR — bumping that to a separate row keeps PRs scoped (see Backlog sync). |
| **Validation** | New unit tests in `WorkspaceToolsIntegrationTests.cs`. Manual: invoke `get_source_text` against `EncodingHelper.cs` on Jellyfin with `startLine=2021, endLine=3021` and assert the response is ~30 KB rather than 384 KB. |
| **Performance review** | Baseline: `get_source_text` on `EncodingHelper.cs` returns 384,113 chars regardless of range. After fix with `startLine=2021, endLine=3021`: ~30 KB returned (~10×–13× smaller payload). Wall-clock time is dominated by JSON serialization, which scales roughly linearly with bytes — expect the same fold-improvement (estimated ~50 ms → ~5 ms). The slicing is O(endLine-startLine), trivial. |
| **Backlog sync** | Closes `get-source-text-line-range-ignored`. Open new lower-priority row `source-file-resource-line-range-parity` to apply the same range support to the `roslyn://workspace/{id}/file/{path}` resource — same root cause but the resource MIME type is `text/x-csharp` so the JSON envelope shape doesn't apply, and a query-param parser is needed; keep that as a separate concern. |

---

### 4. `format-range-preview-nonfunctional` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | The schema at `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs:164-183` matches `code_fix_preview` and friends, so the schema/serialization layer is unlikely to be the culprit. The service at `RefactoringService.cs:222-243` computes `startPosition = text.Lines[startLine-1].Start + (startColumn-1)`. **Two real failure modes:** (a) `startColumn=1` produces `Start + 0` (fine), but `startColumn=0` (a not-uncommon agent input) gives `Start - 1`, and `TextSpan.FromBounds(-1, …)` throws `ArgumentOutOfRangeException`. (b) `Formatter.FormatAsync(document, span, …)` has overloads on Roslyn 5.x — `(Document, TextSpan, OptionSet?, CancellationToken)` and `(Document, TextSpan, SyntaxFormattingOptions?, CancellationToken)` — and the named-arg call `cancellationToken: ct` with `null` options can hit a binding ambiguity at runtime if both are loaded. Zero coverage in `tests/` for this method confirms this code path has never run in CI. |
| **Approach** | (1) Add upfront parameter validation: `startLine >= 1`, `startColumn >= 1`, `endLine >= startLine`, `endColumn >= 1` (when on the same line as `startLine`, also `endColumn >= startColumn`), `endLine <= text.Lines.Count`, `endColumn <= text.Lines[endLine-1].Length + 1` (allow EOL position). Throw `ArgumentException` with a clear message on each violation. (2) Disambiguate the `Formatter.FormatAsync` call by wrapping `span` in an `IEnumerable<TextSpan>` (`new[] { span }`) — that overload is unambiguous. (3) Add a regression test in a new file `tests/RoslynMcp.Tests/FormatRangeServiceTests.cs` covering: valid range round-trip, single-line range, range across blocks, invalid ranges (each rejected with structured error). |
| **Scope** | Modified: `RefactoringService.cs` (~25 LOC). New file: `tests/RoslynMcp.Tests/FormatRangeServiceTests.cs` (~150 LOC, ~6 tests). New files: 1. Modified: 1. |
| **Risks** | If runtime repro reveals the actual error is something other than the two hypotheses above (e.g. a fault in `Formatter` itself for nested syntax), the fix may need to switch to a syntax-tree-rewrite approach (`Formatter.Format` synchronous overload taking a `SyntaxNode`). The tests will surface this — if they fail in CI, we know the diagnosis was wrong and need to dig into the actual exception. |
| **Validation** | New unit tests in `FormatRangeServiceTests.cs`. Manual: invoke `format_range_preview` on Jellyfin's `EncodingHelper.cs` with `startLine=2021, startColumn=1, endLine=2030, endColumn=1` and assert (a) no error, (b) `changes` array is empty (file is presumed already formatted) or contains a small diff. |
| **Performance review** | N/A — correctness fix on a tool that currently always errors. Once functional, expect <100 ms for small ranges, similar to `format_document_preview` scaled by selection size. |
| **Backlog sync** | Closes `format-range-preview-nonfunctional`. Removes the `format_range_preview` row from `schema-drift-jellyfin-audit` (which lumps it in alongside others). |

---

### 5. `goto-type-definition-local-vars` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs:42-79`, the switch coerces every kind to `INamedTypeSymbol`. Two failure modes: (i) for non-named types (`IArrayTypeSymbol`, `ITypeParameterSymbol`, `IPointerTypeSymbol`) the `as` cast returns `null` → empty result; (ii) for `INamedTypeSymbol` types defined in metadata (e.g. `IEnumerable<UserDto>` → `System.Collections.Generic.IEnumerable<T>`), `typeSymbol.Locations` has zero `IsInSource` entries, the `SpecialType.None` guard at line 63 is true (`SpecialType` is `System_Collections_Generic_IEnumerable_T` which IS a special type for some collections, but most generics fall through), so the function returns `[]` with no descriptive message. Audit Phase 14 confirmed: `UserController.cs:98:13` (a local `var users = …` of type `IEnumerable<UserDto>`) returns "No type definition found." |
| **Approach** | Restructure `GoToTypeDefinitionAsync`: (1) extract the type symbol from `symbol` using a switch that returns `ITypeSymbol` (not just `INamedTypeSymbol`) — handle `IArrayTypeSymbol.ElementType`, `ITypeParameterSymbol` (return type-parameter source), `IPointerTypeSymbol.PointedAtType`. (2) Build a list of candidate types: the resolved type itself, plus its type arguments if it is a constructed generic and the constructed type has no source locations. So for `IEnumerable<UserDto>` we navigate to `UserDto`. (3) Collect all `IsInSource` locations across candidates; return them. (4) If still empty after the candidate walk, throw a structured error with the type name and reason ("type defined in metadata; no in-source location, and no in-source type arguments to fall back to"). |
| **Scope** | Modified: `SymbolNavigationService.cs` (~50 LOC: refactor of `GoToTypeDefinitionAsync` only). Tests: extend `tests/RoslynMcp.Tests/SemanticExpansionTests.cs` (or a more specific file if it exists) with cases for: local of named generic type whose argument is in source, local of array type, local of `IEnumerable<T>` from BCL with source-defined `T`, local of `int` (BCL primitive — keep the existing descriptive error). New files: 0. Modified: 2. |
| **Risks** | (a) Returning multiple locations when both the constructed type and a type argument are in source (e.g. `Dictionary<MyKey, MyValue>` where both are user-defined). The existing API returns a `IReadOnlyList<LocationDto>` so multiple results are already supported by the schema; ranking should put the constructed type first if it has any source location, else type arguments in order. (b) Type parameters (`T`) — should resolve to the declaring method/class' type parameter location. Acceptable; tested. |
| **Validation** | New unit tests in `SemanticExpansionTests.cs`. Manual: navigate from a Jellyfin local of type `IEnumerable<UserDto>` to `UserDto` source. |
| **Performance review** | N/A — correctness fix, no hot-path changes. Single additional `OfType<INamedTypeSymbol>().TypeArguments` walk per call, microseconds. |
| **Backlog sync** | Closes `goto-type-definition-local-vars`. |

---

### 6. `diagnostics-resource-timeout` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | The MCP resource `roslyn://workspace/{id}/diagnostics` at `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs:69-79` calls `IDiagnosticService.GetDiagnosticsAsync(workspaceId, null, null, null, null, ct)` with no pagination, then JSON-serializes the entire result. On Jellyfin (3433 diagnostics) the full payload is ~2 MB and the underlying compute is 29–35 s. The companion tool `project_diagnostics` at `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:53-56` enforces `limit=200` at the tool layer; the resource bypasses that. |
| **Approach** | Two changes. (1) Apply a hard cap inside the resource: take only the first 500 diagnostics from each bucket (workspace/compiler/analyzer), wrap in an envelope `{ totalErrors, totalWarnings, totalInfo, returnedDiagnostics, hasMore, paginationNote }`. The resource cannot accept query parameters in MCP, so the cap is fixed. (2) Add an explicit `paginationNote` directing callers to the `project_diagnostics` tool when `hasMore` is true (because the resource cannot accept `offset`). (3) **Also** add a default `severityFilter: "Warning"` floor when no filter is supplied to the resource — Info diagnostics are rarely actionable and dominate Jellyfin's count. (Tool callers retain the existing `severity` parameter for full control.) |
| **Scope** | Modified: `WorkspaceResources.cs` (~30 LOC). New tests in `tests/RoslynMcp.Tests/WorkspaceResourceTests.cs` (covers both the new envelope and the Warning floor). New files: 0. Modified: 2. |
| **Risks** | (a) Breaking change to the resource shape: callers that consumed the raw `DiagnosticsResultDto` from this resource will break. Per the constraints document this is acceptable — we are not maintaining backward compatibility on response shapes. Documented in the resource description. (b) The Warning floor hides Info diagnostics from the resource entirely; callers needing Info must use the tool. Documented. |
| **Validation** | New unit tests. Manual: read `roslyn://workspace/{id}/diagnostics` against Jellyfin; assert response under 500 KB and ~10 s instead of timing out. |
| **Performance review** | Baseline: resource times out (>30 s) on Jellyfin per audit Phase 15. After fix: bounded by `IDiagnosticService.GetDiagnosticsAsync` Warning-only execution time, expected ~10–15 s on Jellyfin (skip Info adds bulk pruning). The 500-row serialization cap brings JSON size from ~2 MB to ~250 KB. |
| **Backlog sync** | Closes `diagnostics-resource-timeout`. Cross-references `project-diagnostics-large-solution-perf` (item 7) which tackles the underlying compute. |

---

### 7. `project-diagnostics-large-solution-perf` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:35-105`, `GetDiagnosticsAsync` always runs both `compilation.GetDiagnostics(ct)` (compiler) AND `compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct)` (analyzers) for every project, regardless of `severityFilter`. Analyzers dominate the cost: on Jellyfin (40 projects, 3433 diagnostics) the call takes 29–35 s (audit §14.3 + stress test 1.4). The unfiltered diagnostic objects are cached in `_diagnosticCache` only for the *detail-lookup* path (line 72-77) — repeat full-scan callers re-run analyzers every time even with the same `workspaceVersion`. |
| **Approach** | Two compounding optimizations. (a) **Result cache keyed on `(workspaceVersion, projectFilter, fileFilter, severityFilter, diagnosticIdFilter)`**: compute a tuple key, store the `DiagnosticsResultDto` in a per-workspace `ConcurrentDictionary`, invalidate on workspace-version bump (the existing `WorkspaceClosed` cache invalidator pattern from `CompilationCache` applies). Subsequent identical calls return in <50 ms. (b) **Skip analyzer pass when severityFilter exceeds all loaded analyzer severities**: if `severityFilter == Error`, gather analyzer descriptors (cheap — already enumerated by `AnalyzerInfoService`) and skip the entire analyzer pass when no descriptor has `DefaultSeverity >= Error`. The compiler pass alone is typically 2–5 s on Jellyfin. |
| **Scope** | Modified: `DiagnosticService.cs` (~80 LOC: cache field + lookup + invalidation hook + analyzer-skip path). New tests in `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`: cache hit returns identical result, cache invalidates on `workspace_reload`, severity=Error skips analyzer pass and timing reflects it. New files: 0. Modified: 2. |
| **Risks** | (a) Cache memory: each cached entry is the full DTO list; on a 3433-diagnostic solution that is ~2 MB. Cap at 8 entries per workspace (LRU eviction) — same order of magnitude as `PreviewStoreOptions.MaxEntries`. (b) Analyzer-skip correctness: a project may load custom analyzers whose default severity differs from `IsEnabledByDefault`. Use `descriptor.DefaultSeverity` AND respect any `.editorconfig` overrides we can inspect. To stay conservative on the first pass: only skip analyzers when `severityFilter == Error` AND no loaded descriptor has `DefaultSeverity == Error`; otherwise, run the analyzer pass. (c) Cache invalidation must hook `IWorkspaceManager.WorkspaceClosed` and `WorkspaceManager.IncrementVersion` (called from `LoadIntoSessionAsync` and `ReloadAsync`). Re-use the same `WorkspaceClosed` event the `CompilationCache` already subscribes to. |
| **Validation** | Tests above. Manual: re-run Jellyfin Phase 1.4 (`project_diagnostics` solution-wide, no filter) twice — first call reproduces 29–35 s baseline; second call should be <100 ms (cache hit). With `severity=Error` first call should be <5 s (analyzer pass skipped). |
| **Performance review** | Baseline (Jellyfin Phase 1.4): 35553 ms cold, no filters. Targets after fix: cold call **without** severity filter ~25–30 s (compiler + analyzers both run); cold call **with** `severity=Error` ~3–5 s (analyzer pass skipped); warm call (cache hit) <100 ms. Brings the call inside the ≤30 s Analysis budget for the most common use (severity=Error or repeat scans). |
| **Backlog sync** | Closes `project-diagnostics-large-solution-perf`. Reduces severity of `diagnostics-resource-timeout` (item 6) by speeding up the underlying call — but the pagination fix in item 6 is still needed to bound payload size. |

---

### 8. `code-fix-providers-missing-ca` (P3)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:245-302`, `PreviewCodeFixAsync` is hardcoded to handle only `CS8019 / remove_unused_using`. Every other diagnostic ID throws `InvalidOperationException("Diagnostic '…' does not have a supported curated code fix.")`. The plumbing for loading providers from Microsoft.CodeAnalysis.CSharp.Features and from project analyzer references already exists in `FixAllService.LoadCodeFixProviders` (line 461-497) and `FixAllService.LoadCodeFixProvidersFromAnalyzerReferences` (line 389-427). The fix is to extract that loader into a reusable service and have `PreviewCodeFixAsync` use it. |
| **Approach** | (1) Extract a new internal service `CodeFixProviderRegistry` (in `src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs`) that exposes `IReadOnlyList<CodeFixProvider> GetProvidersFor(string diagnosticId, Solution? scopedSolution = null)`. It caches the static IDE/Features providers (one-time `Lazy` load) and lazily loads project-analyzer providers per-solution (keyed on the set of analyzer-ref paths). (2) Refactor `FixAllService` to take this registry via constructor injection — drop the local `LoadCodeFixProviders` / `LoadCodeFixProvidersFromAnalyzerReferences` methods. (3) Refactor `RefactoringService.PreviewCodeFixAsync` to: look up a provider via the registry; if found, build a `CodeFixContext` with the actual diagnostic at the position (similar to `FixAllService.GetEquivalenceKeyAsync`); register code actions; pick the first action (or the one matching `fixId` when supplied); apply it via `action.GetOperationsAsync(ct)` and return the resulting diff. (4) Wire the registry in `ServiceCollectionExtensions`. |
| **Scope** | New: `src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs` (~120 LOC). Modified: `FixAllService.cs` (delete ~120 LOC of duplicated loader, inject registry), `RefactoringService.cs` (rewrite `PreviewCodeFixAsync` ~80 LOC), `ServiceCollectionExtensions.cs` (one DI line). New tests: `tests/RoslynMcp.Tests/CodeFixProviderRegistryTests.cs` (~120 LOC): assert IDE0005 fix loads, assert CA1822 fix loads (when NetAnalyzers is present in fixture), assert unknown diagnostic returns null. Extend `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs` for end-to-end. New files: 2. Modified: 3. |
| **Risks** | (a) Some Roslyn IDE fix providers require constructor parameters that cannot be satisfied via reflection (existing `LoadCodeFixProviders` already has a `try { Activator.CreateInstance } catch` and silently skips — keep that behavior, document the gap as `GetAlternativeToolHint` already does). (b) `code_fix_preview` returning multiple actions per diagnostic: today's contract returns a single action; preserve that by picking the first when `fixId` is null, with a hint to use `get_code_actions` for the full list. (c) Equivalence-key mismatch between providers of same diagnostic (e.g. dual implementations in IDE vs Features) — accept first-match, document. |
| **Validation** | Tests. Manual: against a Jellyfin file with CA1822 (static method candidate), run `code_fix_preview` and assert a non-empty diff is returned. Against an IDE0005, same. |
| **Performance review** | Baseline: most calls fail at <10 ms with the hardcoded throw. After fix: first call per workspace pays a one-time analyzer-fix-provider load (~200–500 ms across all referenced analyzer assemblies); subsequent calls <100 ms. Acceptable cost given the tool was previously useless for >95% of diagnostic IDs. |
| **Backlog sync** | Closes `code-fix-providers-missing-ca`. Updates `GetAlternativeToolHint` in `FixAllService.cs:533-546` to no longer claim "could not be loaded" for diagnostics that now have providers. |

---

### 9. `apply-project-mutation-whitespace` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:82-95` (and the analogous `RemoveProjectReference` at line 123-144, `RemoveTargetFramework` etc.), the code calls `element.Remove()` on an `XElement` parsed with `LoadOptions.PreserveWhitespace`. The text node before/after the element (typically `"\n    "`) is preserved, leaving a blank line where the element used to be. An add→remove round-trip therefore does **not** restore the file to its pre-add state. |
| **Approach** | Add a private helper `RemoveElementWithSurroundingWhitespace(XElement element)` in `ProjectMutationService.cs` that: locates the element's `PreviousNode` and `NextNode`, removes only the leading whitespace text node up to and including the newline immediately before the element (so the line the element occupied disappears entirely), then calls `element.Remove()`. If removing the element leaves the parent `ItemGroup`/`PropertyGroup` with no children but only whitespace, also remove the empty group (so `<ItemGroup />` doesn't linger after the last `<PackageReference>`). Apply at all four call sites: `PreviewRemovePackageReferenceAsync`, `PreviewRemoveProjectReferenceAsync`, `PreviewRemoveCentralPackageVersionAsync`, `PreviewRemoveTargetFrameworkAsync`. |
| **Scope** | Modified: `ProjectMutationService.cs` (~30 LOC for helper + 4 callsites). Tests: extend `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs` with round-trip tests (add → remove → assert byte-identical to baseline) for each of the four removal types. New files: 0. Modified: 2. |
| **Risks** | (a) Edge case: element preceded by an XML comment (`<!-- -->`) — should NOT remove the comment. The helper inspects only `XText` nodes, not `XComment`. (b) Edge case: element is the only child of a multi-line `ItemGroup` — remove the group too, but keep its surrounding whitespace cleanup symmetrical. (c) Edge case: file uses CRLF line endings; `\r\n` must be matched, not just `\n`. Use `Environment.NewLine`-agnostic regex on the trailing whitespace. |
| **Validation** | Round-trip tests. Manual: add then remove a `PackageReference` in a Jellyfin csproj, diff-check restores to original. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **Backlog sync** | Closes `apply-project-mutation-whitespace`. |

---

### 10. `analyze-data-flow-inverted-range` (P4)

| Field | Content |
|-------|---------|
| **Diagnosis** | At `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:20-57` (`AnalyzeDataFlowAsync`) and `:59-142` (`AnalyzeControlFlowAsync`), no upfront validation of `startLine <= endLine`. When inverted, `startLine0 = startLine - 1, endLine0 = endLine - 1` keep the inversion; the predicate `lineSpan.StartLinePosition.Line >= startLine0 && <= endLine0` is never true; we fall through to the misleading `"No statements found in the line range 200-100"` error. |
| **Approach** | Add a single-line guard at the top of `ResolveAnalysisRegionAsync` (`FlowAnalysisService.cs:156-251`): `if (startLine > endLine) throw new ArgumentException($"startLine ({startLine}) must be ≤ endLine ({endLine}).", nameof(startLine));`. Also validate `startLine >= 1` and `endLine >= 1`. Both `AnalyzeDataFlowAsync` and `AnalyzeControlFlowAsync` use this resolver, so one fix covers both. |
| **Scope** | Modified: `FlowAnalysisService.cs` (~5 LOC). Tests: extend `tests/RoslynMcp.Tests/FlowAnalysisServiceTests.cs` with inverted-range, zero-line, negative-line cases. New files: 0. Modified: 2. |
| **Risks** | None — pure validation addition, fail-fast behavior, no semantic change to valid inputs. |
| **Validation** | New unit tests. Manual: invoke `analyze_data_flow` with `startLine=200, endLine=100` and assert the `ArgumentException` carries the structured message. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **Backlog sync** | Closes `analyze-data-flow-inverted-range`. |

---

## Cross-cutting observations

- **Perf smell observed but NOT fixed in this batch:**
  - `DiagnosticService` and `CompilationCache` re-enumerate `solution.Projects` per call without a project-id index. On 100+ project solutions this becomes O(P²) at the call sites that look up a single project by name. New backlog row recommended: `solution-project-index-by-name` (P4) — add a per-`workspaceVersion` `Dictionary<string, Project>` index in `WorkspaceManager` exposed via `IWorkspaceManager.GetProject(workspaceId, projectName)`.
  - `SecurityDiagnosticService.LoadFromAnalyzerReferences` (line 121) iterates all projects' references even when only checking for analyzer presence (boolean output). Could short-circuit on first match. Low impact, separate row not needed unless seen in profiling.

- **Items intentionally batched into one PR per item, NOT bundled:** Following the "one PR = one story" rule. Items 1, 2, 7, 8 each touch performance-sensitive or load-time code; bundling would make any rollback messy if a single fix regresses Jellyfin baselines.

- **All 10 items collectively close 6 P2/P3 backlog rows and 2 P4 rows.** No item depends on another's apply for compilation; they can ship in any order. Suggested order: 1, 2 first (P2 unblockers), then 3–8 (P3 by impact), then 9, 10 (P4 cleanup).

---

## Final per-PR todo template

Each PR plan must include this block at the bottom:

```
- [ ] Implement fix per `ai_docs/reports/20260414T220000Z_top10-remediation-plan.md` § <id>
- [ ] Tests pass: `just test`
- [ ] CI gate green: `just ci`
- [ ] Re-run Jellyfin baseline if perf-sensitive (items 1, 6, 7)
- [ ] backlog: sync ai_docs/backlog.md (remove the closed row, update any cross-references called out in the plan's "Backlog sync" field)
```
