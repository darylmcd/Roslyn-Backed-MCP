# Backlog sweep plan — 20260519T193650Z

**Generated:** 2026-05-19T19:36:50Z
**Backlog snapshot:** 2026-05-19T19:31:17Z (after firewallanalyzer aggregator splits via PR #850)
**Schema version:** 3 (prepare-extended)
**Initiative count:** 15 (11 P2 + 4 P3, all child rows from the 2026-05-16 firewallanalyzer audit)

## Selection notes

count=15 cap requested; 23 sweep-actionable rows available after the gh #768 + gh #769 split (PR #850 landed 23 child rows replacing 2 aggregators).

**Selected (15):** all 11 P2 (Medium, audit §13.3 + §13.9–13.18) + top 4 P3 (Low, by correctness/blast-radius/feasibility ranking): #12 `remove-preview-family-invalidoperation-for-missing-items`, #13 `document-symbols-vs-symbol-info-record-kind-disagreement`, #14 `get-msbuild-properties-vs-workspace-reload-outputtype-mismatch`, #15 `source-file-lines-off-by-one-marker-count`.

**Deferred (8 P3, next sweep):** `find-duplicate-helpers-framework-wrapper-filter-leak`, `compile-check-file-filter-scope-narrowing-inconsistency`, `get-nuget-dependencies-summary-cpm-literal`, `get-cohesion-metrics-always-null-lifecycle-pattern`, `add-pragma-suppression-crlf-in-lf-file`, `find-type-mutations-error-template-diverges-from-siblings`, `dependency-inversion-preview-newline-before-comma-formatting`, `go-to-definition-off-identifier-misleading-message`.

**Excluded:** 4 Reserved good-first-issue rows (planner Step 1 hard-skip), 1 track-only weak-evidence row (`tool-surface-pagination-or-tool-sets`), 6 Defer rows.

**Deepener summary:** 15/15 ok. 9 of 15 judgmentHeavy. **11 of 15 anchorStale** — this audit batch (filed 2026-05-16) cited many paths that have since been refactored; deepeners verified live anchors. **Notable:** initiative #14 has `productionFilesTouched: 0` — the underlying bug was already fixed by a prior `project-graph-output-type-misreports-sdk-defaulted-exe` initiative; this row adds a cross-surface regression test only.

**Parent-tracker discipline:** every child row references gh #768 / gh #769 in plain text only — initiative-executor will NOT auto-`Fixes` them on child PR merge. Parent trackers close manually when all children ship.

## Initiatives

### 1. find-references-static-extension-host-blind-spot

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-references-static-extension-host-blind-spot` |
| Diagnosis | All six type-level surfaces (`find_references`, `find_consumers`, `find_type_consumers`, `find_type_usages`, `symbol_impact_sweep`, `impact_analysis`) share the same root cause: each calls `SymbolFinder.FindReferencesAsync(symbol, solution, ct)` where `symbol` is the `INamedTypeSymbol` for the static class. Roslyn's reference index on a type only records syntactic sites where the **type name token** appears. Extension-method call sites (e.g. `app.MapImportEndpoints()`) bind to the **method** `MapImportEndpoints`, not to the containing type `ImportEndpoints` — the type token is absent, so the index returns 0 locations. Code paths confirmed stale-anchor-free: `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:30` (`SymbolFinder.FindReferencesAsync`); `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:27` (same call); `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:53` (same call); `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:290` (`FindTypeUsagesAsync`, same call); `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs:47` (delegates to `_references.FindReferencesAsync`). Anchor `src/RoslynMcp.Roslyn/Services/ImpactAnalysisService.cs` cited in backlog is stale — this file does not exist; `impact_analysis` routes through `MutationAnalysisService`. |
| Approach | **1. `ConsumerAnalysisService.FindConsumersAsync`** (`src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs`): after `SymbolFinder.FindReferencesAsync` yields 0 `refLocations` AND the resolved symbol is a `static` `INamedTypeSymbol` with public extension members (i.e. `IsStatic == true` and any member is an `IMethodSymbol` with `IsExtensionMethod == true`), iterate the type's public `IMethodSymbol` members and call `SymbolFinder.FindReferencesAsync` on each, union the results into `refLocations`, and continue into the existing classification loop unchanged. **2. `ImpactSweepService.BuildSuggestedTasks`** (`src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs`): extend the zero-impact branch to emit `"Static extension-host class — type-level reference index is blind to extension-method call sites. Use callers_callees(<MemberName>) on each public member to find consumers."` when the resolved symbol is a static extension-host class. **3. Test fixture**: add `SampleLib/ExtensionHostFixture.cs` consumed via `app.MapRoutes()`. |
| Scope | Production files (3): `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs`, `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs`, `samples/SampleSolution/SampleLib/ExtensionHostFixture.cs`. Test files (1): `tests/RoslynMcp.Tests/ConsumerAnalysisTests.cs` (extend). `find_type_consumers` / `find_type_usages` blind spot deferred to follow-on to stay within Rule 3. |
| Tool policy | edit-only |
| Estimated context cost | 38000 |
| Risks | (1) Member-union fallback must deduplicate by `(FilePath, SourceSpan)`. (2) `ImpactSweepService` suggestion must degrade gracefully if member resolution fails. (3) `TypeConsumersService` / `MutationAnalysisService.FindTypeUsagesAsync` are intentionally NOT fixed here (Rule 3 cap; same root cause but different code paths). (4) Stale anchor `ImpactAnalysisService.cs` does not exist; cosmetic discrepancy only. |
| Validation | New test `FindConsumers_ExtensionHostClass_AggregatesToMemberConsumers`; existing `FindConsumers_StaticClass_ClassifiesAsStaticMemberAccess` must still pass; `ImpactSweepService` test asserts `SuggestedTasks` contains `callers_callees` hint. |
| Performance review | N/A — correctness fix, cold-path only. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `find_consumers` and `symbol_impact_sweep` returning empty results for static extension-host classes whose members are consumed exclusively via extension-method syntax (e.g. `app.MapImportEndpoints()`). `find_consumers` now aggregates member-level consumers as a fallback; `symbol_impact_sweep` emits a `suggestedTasks` hint pointing to `callers_callees`. Fixes gh #768 §13.3. |
| Backlog sync | Close rows: [`find-references-static-extension-host-blind-spot`]. Parent tracker gh #768 §13.3 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates (member-union fallback):** `ConsumerAnalysisService.cs:27` is the `SymbolFinder.FindReferencesAsync` call producing 0 hits for static extension-host types. Insert the fallback between line 28 (`refLocations` materialized) and the `projectFilter` block at lines 32-38 — gate on `refLocations.Count == 0 && symbol is INamedTypeSymbol { IsStatic: true } host && host.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod)`, iterate `host.GetMembers().OfType<IMethodSymbol>()` (filter `DeclaredAccessibility == Accessibility.Public`), call `FindReferencesAsync` per member, and union into `refLocations` BEFORE the project filter runs. Dedupe by `(loc.Document?.FilePath, loc.Location.SourceSpan)`.
- **SuggestedTasks hint coordinates:** `ImpactSweepService.BuildSuggestedTasks` at `ImpactSweepService.cs:301-315`. The zero-impact branch is the `if (tasks.Count == 0)` at line 312. Plumb the resolved symbol (or a precomputed `isStaticExtensionHost` bool) into `BuildSuggestedTasks` — change signature or compute the flag at the call site (line 84). The hint emits ABOVE the `"No impact detected"` fallback, not instead of it.
- **Test target:** extend `ConsumerAnalysisTests.cs` — add `FindConsumers_ExtensionHostClass_AggregatesToMemberConsumers` mirroring `FindConsumers_StaticClass_ClassifiesAsStaticMemberAccess` at lines 63-84 (same `SymbolLocator.ByMetadataName` shape, same `SharedWorkspaceTestBase` setup).
- **Fixture wiring (correction):** plan's `app.MapRoutes()` example assumes ASP.NET, but SampleApp is a plain console app — DO NOT add `Microsoft.AspNetCore.App` to keep the fixture hermetic. Use an extension-method receiver of a stable type (e.g. `IAnimal` or `string`). Add a consumer line to `samples/SampleSolution/SampleApp/Program.cs` (after line 35) using extension-call syntax — `animals.First().<NewExtension>()`. Existing `AnimalExtensions.cs` is already consumed via `.Describe()` at `Program.cs:12` — assert on the NEW host's metadata name, not on `AnimalExtensions`.
- **Edge cases:** (1) public-only member iteration; (2) dedupe by `(FilePath, SourceSpan)`; (3) zero-extension-member static class must stay a no-op; (4) `projectFilter` must still apply to the unioned set.
- **Negative space:** do NOT modify `TypeConsumersService.cs:53` or `MutationAnalysisService.FindTypeUsagesAsync` at `MutationAnalysisService.cs:290` — same root cause, explicitly deferred for Rule 3 cap. Do NOT touch `ReferenceService.cs:30` (`find_references`) — type-token semantics by design; fix scope is `find_consumers` + `symbol_impact_sweep` only.

</details>

### 2. code-fix-preview-vs-fix-all-preview-shape-inconsistency

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `code-fix-preview-vs-fix-all-preview-shape-inconsistency` |
| Diagnosis | Confirmed: `FixAllService.PreviewFixAllAsync` at `src/RoslynMcp.Roslyn/Services/FixAllService.cs:78-87` returns a structured `FixAllPreviewDto` with `GuidanceMessage` when no provider is found. `RefactoringService.PreviewCodeFixAsync` at `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:611-613` throws `InvalidOperationException("No code fix provider is loaded for diagnostic '…'")` for the same condition. `RefactoringPreviewDto` (in `src/RoslynMcp.Core/Models/RefactoringPreviewDto.cs`) lacks a `GuidanceMessage` field. Backlog cited stale anchors `CodeFixService.cs` and `CodeFixTools.cs` — neither exists; live symbols are in `RefactoringService.cs:548` and `RefactoringTools.cs:119`. |
| Approach | 1. Add nullable `GuidanceMessage` field to `src/RoslynMcp.Core/Models/RefactoringPreviewDto.cs` (mirror `FixAllPreviewDto`). 2. In `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`, replace the `throw new InvalidOperationException` at lines 611-613 with a structured return: `return new RefactoringPreviewDto("", $"No code fix provider…", [], null, guidanceMessage: BuildNoProviderGuidance(diagnosticId))` mirroring `FixAllService.cs:78-87`. 3. Extend `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs` with a no-provider scenario test. |
| Scope | Production files (2): `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`, `src/RoslynMcp.Core/Models/RefactoringPreviewDto.cs`. Test files (1): `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) `RefactoringPreviewDto` is widely consumed; adding nullable field is non-breaking but verify no exhaustive pattern-match callers. (2) CS8019 special-case at lines 599-609 also ends in throw; structured return must come after that branch. (3) Both cited backlog anchors are stale; executor should use the verified live paths. |
| Validation | `mcp__roslyn__compile_check`; `mcp__roslyn__test_run --filter DiagnosticFixIntegrationTests`; manual: call `code_fix_preview` with CA1859 on a clean file → expect non-empty `guidanceMessage`, no exception. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `code_fix_preview` returning an unhandled `InvalidOperationException` when no code fix provider is registered for the requested diagnostic ID. The tool now returns a structured envelope with empty `previewToken` and a `guidanceMessage`, consistent with `fix_all_preview`. Fixes gh #768 §13.9. |
| Backlog sync | Close rows: [`code-fix-preview-vs-fix-all-preview-shape-inconsistency`]. Parent tracker gh #768 §13.9 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates:** mirror `src/RoslynMcp.Roslyn/Services/FixAllService.cs:78-87` — the `provider is null` branch returns `new FixAllPreviewDto(PreviewToken: "", ..., GuidanceMessage: BuildNoProviderGuidance(diagnosticId))`. `BuildNoProviderGuidance` is defined at `FixAllService.cs:622-632` (`internal static`); reuse it directly via `FixAllService.BuildNoProviderGuidance(diagnosticId)` rather than duplicating the string.
- **DTO addition:** append `string? GuidanceMessage = null` as a positional default-valued record parameter to `RefactoringPreviewDto` (last position after `CallsiteUpdates`, `src/RoslynMcp.Core/Models/RefactoringPreviewDto.cs:14-19`). Do NOT reorder existing params.
- **Fix-site shape:** at `RefactoringService.cs:611-613`, replace the throw with `return new RefactoringPreviewDto(string.Empty, $"No code fix provider for '{diagnosticId}'.", Array.Empty<FileChangeDto>(), null, GuidanceMessage: FixAllService.BuildNoProviderGuidance(diagnosticId));`. The CS8019 fallback at lines 599-609 returns *before* this line — keep untouched.
- **Test target:** mirror `FixAllServiceGuidanceTests.cs:147-163` (`PreviewFixAll_NoProviderRegistered_ReturnsGuidance`); add `CodeFixPreview_NoProviderRegistered_ReturnsGuidance` to `DiagnosticFixIntegrationTests.cs`. Use diagnosticId `"ZZZ_NO_PROVIDER_REGISTERED_FOR_THIS_ID"`; assert `PreviewToken == string.Empty`, `Changes.Count == 0`, `GuidanceMessage` contains the diagnosticId.
- **Negative space:** do NOT modify `PreviewRemoveUnusedUsingFallbackAsync` (`RefactoringService.cs:616+`) — internal throws at line 627 are unreachable-edge guards, not the no-provider path. Do NOT change `FixAllPreviewDto` shape.

</details>

### 3. suggest-refactorings-facade-extraction-false-positive

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `suggest-refactorings-facade-extraction-false-positive` |
| Diagnosis | **Stale anchor:** backlog cited `src/RoslynMcp.Roslyn/Services/SuggestRefactoringsService.cs` — does not exist. Actual aggregator is `src/RoslynMcp.Roslyn/Services/RefactoringSuggestionService.cs:97-107`: the cohesion loop emits a `"Split <type>"` suggestion whenever `c.Lcom4Score >= 2 && c.FilePath is not null`. It does not inspect `c.FieldCount` nor `c.LifecyclePattern` (bypass field already present on `CohesionMetricsDto`). Upstream `CohesionAnalysisService.cs:95-96` populates `LifecyclePattern`/`Recommendation` but `DetectLifecyclePattern` only recognises the `"action-triad"` shape — a 0-field facade implementing an interface is not covered. |
| Approach | **Part 1 — extend `DetectLifecyclePattern` in `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`** (~line 196): add a `"facade"` pattern branch when (a) `instanceFields.Count == 0`, (b) `typeSymbol.Interfaces.Length > 0`, (c) every public ordinary method is expression-bodied or single-return. Pass `instanceFields.Count` from the call site (line 95). Extend `BuildRecommendation` (line 230) with `"facade"` case. **Part 2 — guard in `src/RoslynMcp.Roslyn/Services/RefactoringSuggestionService.cs`** (line 97): change loop filter to additionally skip when `c.LifecyclePattern is not null` (respects existing bypass contract). |
| Scope | Production files (2): `CohesionAnalysisService.cs`, `RefactoringSuggestionService.cs`. Test files (0 new, 2 extended): `CohesionAnalysisTests.cs`, `RefactoringSuggestionTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 38000 |
| Risks | (1) Expression-body detection must not false-positive on static utility classes (zero fields by accident) — guarded by the interface-implementation conjunction. (2) `DetectLifecyclePattern` signature change adds a parameter — verify private-static. (3) Aggregator guard skips ALL `LifecyclePattern`-bearing types; document for future kinds. (4) Stale anchor was rewritten. |
| Validation | New `CohesionAnalysisTests` assert `LifecyclePattern == "facade"` + `Recommendation` contains "Facade/adapter"; new `RefactoringSuggestionTests` assert no `Category == "cohesion"` suggestion for `LifecyclePattern=facade` synthetic DTO. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `suggest_refactorings` false-positive: facade/adapter types (zero instance fields, all methods delegate to an injected interface) no longer surface a top-severity "Split" recommendation. `CohesionAnalysisService` now detects the `"facade"` lifecycle pattern and `RefactoringSuggestionService` suppresses cohesion suggestions for any type bearing a `LifecyclePattern` value. Fixes gh #768 §13.10. |
| Backlog sync | Close rows: [`suggest-refactorings-facade-extraction-false-positive`]. Parent tracker gh #768 §13.10 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates:** mirror `CohesionAnalysisService.cs:196-223` (existing `DetectLifecyclePattern` `"action-triad"` branch) — add a `"facade"` arm below the suffix check; also mirror `BuildRecommendation` at `:230-234` (add a `"facade"` case). The signature change must pass `instanceFields.Count` from the call site at `:95` — the field-list is built one line earlier at `:90` (`CollectInstanceFieldsAndProperties`).
- **Expression-body detection:** use `IMethodSymbol.DeclaringSyntaxReferences` and downcast to `MethodDeclarationSyntax` — check `m.ExpressionBody is not null` OR a `Body` containing exactly one `ReturnStatementSyntax`. Reference pattern at `CodeMetricsService.cs:160-177`. Do NOT rely on `FieldCount==0` alone.
- **Test target (correction):** extend `CohesionAnalysisTests.cs` (existing class line 7, `IsolatedWorkspaceTestBase`); mirror `GetCohesionMetrics_ActionTriadPattern_...` at `:263-317`. For `RefactoringSuggestionTests.cs` (`SharedWorkspaceTestBase`, line 5): add a workspace-fixture-based test that writes a real facade type — DO NOT invent a "synthetic DTO" injection path; `TestServiceContainer.cs:275-279` wires the service with concrete deps only.
- **Negative space:** facade detection must require ALL THREE conditions in conjunction (zero instance fields AND `Interfaces.Length > 0` AND all-public-methods expression-bodied/single-return). Do NOT match static utility classes (zero fields but no interfaces).

</details>

### 4. find-duplicated-methods-symmetric-mapper-false-positives

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-duplicated-methods-symmetric-mapper-false-positives` |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/DuplicateMethodDetectorService.cs` — `BucketDocumentMethods` (line 100) buckets every `MethodDeclarationSyntax` by pure AST structure with no semantic awareness. Two false-positive categories result: (1) symmetric `To*`/`From*` mapper pairs collapse into one bucket with `Similarity: 1.0`; (2) xUnit `[Theory]` test methods share identical dispatch shapes and cluster spuriously. `EmitGroupsFromBuckets` (line 148) emits all buckets ≥ 2 with no post-bucket classification. `DuplicatedMethodGroupDto` (`src/RoslynMcp.Core/Models/DuplicatedMethodGroupDto.cs:20`) has no `ClusterKind` discriminator. |
| Approach | (1) `BucketDocumentMethods`: skip methods with `[Theory]` attribute (`EndsWith("Theory")` or `EndsWith("TheoryAttribute")`). (2) `EmitGroupsFromBuckets`: after confirming bucket size ≥ 2, detect symmetric mapper pairs (exactly 2 members, names are strict `To*`/`From*` complements with same stem); downrank `Similarity` and set `ClusterKind: "round-trip-mapper"`. (3) Add `ClusterKind` (nullable string) field to `DuplicatedMethodGroupDto`. Tests: 2 new methods in `tests/RoslynMcp.Tests/DuplicateMethodDetectorTests.cs` (mapper-pair downrank assertion + `[Theory]` exclusion assertion). |
| Scope | Production files (2): `DuplicateMethodDetectorService.cs`, `DuplicatedMethodGroupDto.cs`. Test files (1, extended): `DuplicateMethodDetectorTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | (1) `ClusterKind` is additive/non-breaking but exhaustive pattern-match callers should verify. (2) `To*`/`From*` heuristic is name-based only — pair must be exact 2 members with strict complementary stem to avoid mis-downranking. (3) `[Theory]` attribute check is syntactic — use `EndsWith` for robustness against fully-qualified names. (4) Fanout skipped — surgical edit. |
| Validation | New mapper-pair and `[Theory]` fixtures pass; existing 8 tests still pass; manual check against self-repo confirms reduced false-positive rate. |
| Performance review | N/A — O(attributes) per method scan negligible. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `find_duplicated_methods` clustering xUnit `[Theory]` test methods (now excluded) and symmetric `To*`/`From*` round-trip mapper pairs as copy-paste duplicates. Mapper pairs are now emitted with `clusterKind: "round-trip-mapper"` and a downranked similarity score. Fixes gh #768 §13.11. |
| Backlog sync | Close rows: [`find-duplicated-methods-symmetric-mapper-false-positives`]. Parent tracker gh #768 §13.11 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates:** `[Theory]` exclusion goes inside `BucketDocumentMethods` at `DuplicateMethodDetectorService.cs:109-120` — add the attribute-name check after the `body is null` guard (line 114) and before the `MinLines` gate (line 120). Mapper-pair downrank goes inside `EmitGroupsFromBuckets` at `DuplicateMethodDetectorService.cs:148-175` — after `members.Count < 2` continue (line 158), before DTO emission (line 161), detect `members.Count == 2 && To*/From*` complementary stems and set local `similarity`/`clusterKind` variables.
- **Test target:** extend `DuplicateMethodDetectorTests.cs` (NO new file) — add two new `[TestMethod]` methods mirroring `FindDuplicatedMethods_OverloadsWithIdenticalBodies_Cluster` at line 115. The class uses MSTest (`[TestMethod]`), but the `[Theory]` exclusion fixture must embed the xUnit attribute inside the test source-string (syntactic check — no xUnit reference needed). Use existing `BuildServiceWithSource` helper at line 384.
- **Edge cases:** (1) `[TheoryAttribute]` (fully-qualified form) — both endings must match; (2) `To`/`From` with 3+ members in same bucket — must NOT downrank (exactly-2-only heuristic); (3) `ToX`/`FromY` with mismatched stems — must NOT downrank.
- **Negative space:** do NOT touch `Canonicalize` (lines 231-277) or `RankGroups` (line 182). `DuplicatedMethodMemberDto` stays unchanged — only the group-level DTO grows `ClusterKind`.

</details>

### 5. test-coverage-fail-fast-on-missing-coverlet

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `test-coverage-fail-fast-on-missing-coverlet` |
| Diagnosis | **Stale anchor:** backlog cited `src/RoslynMcp.Roslyn/Services/TestCoverageService.cs` — does not exist. Actual implementation: `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs`. Root cause: `RunTestCoverageCore` calls `FindTestProjectsWithoutCoverlet` (line 64); line 65 checks `if (testProjectsLackingCoverlet.Count > 0)` and fails the whole call with `CoverletMissing`. `TestCoverageResultDto` (`src/RoslynMcp.Core/Models/TestCoverageDto.cs`) currently has no `CoverageGaps` field. |
| Approach | 1. Add `IReadOnlyList<string>? CoverageGaps = null` parameter to `TestCoverageResultDto` (after `FailureEnvelope`). 2. Refactor `RunTestCoverageCore` to split projects into `withCoverlet` and `withoutCoverlet`; only fail-fast when `withCoverlet.Count == 0`. Otherwise run per-project sequential coverage on `withCoverlet`, aggregate Cobertura results, set `CoverageGaps = withoutCoverlet.Select(p => p.Name)`. 3. Update `SerializeWithDeprecation` to include `coverageGaps`. 4. New test class `TestCoveragePartialCoverletTests.cs` using `FakeWorkspaceManager` + `StaticDotnetCommandRunner` for mixed-coverlet scenarios. |
| Scope | Production files (2): `TestCoverageDto.cs`, `TestCoverageTools.cs`. Test files (1 new): `TestCoveragePartialCoverletTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | (1) Aggregation strategy across multiple Cobertura outputs: per-project `ModuleCoverageDto` entries are cleanest. (2) `coverageGaps` is additive but callers doing strict JSON schema checks may be surprised. (3) Fixture must use fake runners to stay hermetic. |
| Validation | New test assertions: `success=true`, `coverageGaps` array contains skipped names; existing `TestCoverageFailureEnvelopeTests` unchanged; regression: all-projects-lack-coverlet still fail-fasts. |
| Performance review | N/A — per-project sequential coverage only triggered in mixed-coverlet path. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `test_coverage` failing the entire call when some test projects lack `coverlet.collector`. Projects without the collector are now skipped and listed in a new `coverageGaps` field; partial coverage is returned with `success=true`. Fixes gh #768 §13.12. |
| Backlog sync | Close rows: [`test-coverage-fail-fast-on-missing-coverlet`]. Parent tracker gh #768 §13.12 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates:** the existing fail-fast emit point is `TestCoverageTools.cs:65-81` (the `testProjectsLackingCoverlet.Count > 0` branch — keep for the all-projects-lacking case). The single serializer is `SerializeWithDeprecation` at `TestCoverageTools.cs:201-213` — add `coverageGaps = result.CoverageGaps` to the anonymous envelope there or the new field will not reach JSON. `FindTestProjectsWithoutCoverlet` at `TestCoverageTools.cs:221-244` already returns the project-name list you need to partition; refactor in place rather than adding a parallel helper.
- **Test doubles (correction):** create new file `tests/RoslynMcp.Tests/TestCoveragePartialCoverletTests.cs` (do NOT extend `TestCoverageFailureEnvelopeTests.cs`). The test doubles (`PassthroughGate`, `FakeWorkspaceManager`, `StaticDotnetCommandRunner`) at `TestCoverageFailureEnvelopeTests.cs:150-258` are `private sealed` nested classes — NOT shared infrastructure. Duplicate them as nested classes in the new file. Override `FakeWorkspaceManager.BuildStatus` to return `Projects` with mixed `IsTestProject=true` entries pointing at on-disk temp `.csproj` files (some containing the literal `coverlet.collector`, some not) — `FindTestProjectsWithoutCoverlet` at line 233-237 reads from disk.
- **Edge cases:** (1) mixed coverlet → `success=true`, `coverageGaps` populated, fail-fast NOT triggered; (2) all-projects-lack-coverlet regression → still fails with `CoverletMissing` envelope (existing path preserved); (3) `coverageGaps` field present in JSON via `JsonDocument` parse.
- **Negative space:** do NOT touch `FailureEnvelope.MissingPackages` (in `TestCoverageDto.cs:30`) — that field is the fail-fast surface for the all-missing case; `CoverageGaps` is the new top-level success-with-skips surface and must NOT live inside the failure envelope. Do NOT modify the OCE/Exception catch blocks (`TestCoverageTools.cs:133-166`) — owned by `test-coverage-timeout-failure-envelope` initiative.

</details>

### 6. set-conditional-property-preview-allowlist-narrowness

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `set-conditional-property-preview-allowlist-narrowness` |
| Diagnosis | Confirmed in `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:17-23`: `AllowedProperties` `HashSet<string>` contains exactly `{"Nullable", "LangVersion", "ImplicitUsings", "TargetFramework"}`. Both `PreviewSetProjectPropertyAsync` (line 172) and `PreviewSetConditionalPropertyAsync` (line 393) call `ValidateAllowedProperty` (line 640) which throws for any name absent from this set. Five common per-config properties (`DefineConstants`, `Optimize`, `DebugType`, `NoWarn`, `TreatWarningsAsErrors`) are all rejected. Tool description strings at `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs:115,176` also hard-code the narrow allowlist. |
| Approach | 1. Expand `AllowedProperties` `HashSet` initializer (lines 17-23) to add the 5 new entries. 2. Update `[Description]` attributes on `propertyName` parameter for both tools (lines 115, 176). 3. Add 1 new `[TestMethod]` to `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs` covering DefineConstants + Optimize + NoWarn (mirror `Set_Conditional_Property_Preview_And_Apply_Adds_Conditional_Property_Group` at line 324). |
| Scope | Production files (2): `ProjectMutationService.cs`, `ProjectMutationTools.cs`. Test files (1): `ProjectMutationIntegrationTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | (1) Expanding allowlist also unblocks `set_project_property_preview` (shared `ValidateAllowedProperty`); intentional but note in PR. (2) `DefineConstants` is semicolon-delimited; this fix doesn't add merge semantics. (3) Chose expand-allowlist path over `force=true` opt-in alternative. (4) Fanout skipped — private-static field. |
| Validation | `mcp__roslyn__compile_check`; `mcp__roslyn__test_run --filter ProjectMutationIntegrationTests`; manually verify `set_conditional_property_preview` with `DefineConstants` + `'$(Configuration)'=='Release'` returns valid preview. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `set_conditional_property_preview` and `set_project_property_preview` rejecting common per-config properties (`DefineConstants`, `Optimize`, `DebugType`, `NoWarn`, `TreatWarningsAsErrors`) with "not in allowlist". Tool descriptions updated to reflect the expanded set. Fixes gh #768 §13.13. |
| Backlog sync | Close rows: [`set-conditional-property-preview-allowlist-narrowness`]. Parent tracker gh #768 §13.13 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates:** `AllowedProperties` HashSet at `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:17-23` (currently `Nullable`, `LangVersion`, `ImplicitUsings`, `TargetFramework`). Add the 5 new entries here; the shared `ValidateAllowedProperty` at line 640 needs no change. Tool description strings to update: `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs:115` (`PreviewSetProjectProperty.propertyName`) and `:176` (`PreviewSetConditionalProperty.propertyName`).
- **Test target:** add a single `[TestMethod]` to `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`; mirror `Set_Conditional_Property_Preview_And_Apply_Adds_Conditional_Property_Group` at line 324. Do NOT create a new test file.
- **Wave-conflict note (#6 ↔ #12):** shares `ProjectMutationService.cs` with initiative #12. The two edits are in disjoint regions (lines 17-23 here vs. lines 109/165/330/354/370/465 in #12) but the executor MUST schedule #6 and #12 in different parallel waves to avoid merge serialization friction.
- **Negative space:** do NOT touch lines 109/165/330/354/370/465 in `ProjectMutationService.cs` — those are #12's absent-item throws. Do NOT alter `ValidateAllowedProperty` body at line 640 or `SupportedConditionPattern` at line 13 — expansion is HashSet-only.

</details>

### 7. get-completions-filtertext-doesnt-promote-in-scope-members

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-completions-filtertext-doesnt-promote-in-scope-members` |
| Diagnosis | Root cause: `src/RoslynMcp.Roslyn/Services/CompletionService.cs:53` calls Roslyn's `CompletionService.GetCompletionsAsync(document, position, cancellationToken: ct)` with NO `CompletionTrigger`. At a general statement position (no preceding `.`), Roslyn returns the global accessible-type set but does NOT include instance-member completions because there is no receiver expression. The `InScopeRank` sort at lines 77-80 correctly promotes Methods over Classes but cannot promote items Roslyn never emitted. The existing test `GetCompletions_Ranking_BoostsInScopeBeforeExternalTypes` exercises a member-access position (post-`.`), not a general statement position. |
| Approach | 1. `src/RoslynMcp.Core/Services/ICompletionService.cs`: add `char? triggerCharacter` parameter. 2. `src/RoslynMcp.Roslyn/Services/CompletionService.cs`: at line 53, when `triggerCharacter` is non-null pass `CompletionTrigger.TriggerOnInsertion(triggerCharacter.Value)`. 3. `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`: add optional `[Description] char? triggerCharacter = null` parameter to `GetCompletions`; update tool description to document the member-access constraint. New test in `ServiceCoverageTests.cs` (general position + `triggerCharacter='.'`). |
| Scope | Production files (3): `ICompletionService.cs`, `CompletionService.cs`, `SymbolTools.cs`. Test files (1 extended): `ServiceCoverageTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | (1) Verify `CompletionTrigger.TriggerOnInsertion` overload exists in referenced Roslyn package. (2) Interface signature change — verify single implementation. (3) "Without trigger = no method-tier candidates" assertion is Roslyn-version-sensitive; weaken if needed. (4) Fanout skipped — 3-file surgical edit. |
| Validation | `mcp__roslyn__compile_check`; `mcp__roslyn__test_run --filter GetCompletions`; manual: confirm `get_completions(triggerCharacter='.')` returns `ToString` ranked before `ToBase64Transform`. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `get_completions` in-scope member ranking at member-access positions: added optional `triggerCharacter` parameter that, when set to `'.'`, passes `CompletionTrigger.TriggerOnInsertion('.')` to Roslyn so method-tier candidates (locals, parameters, members) are included and ranked before namespace-qualified external types. Tool description updated to document the member-access requirement. Fixes gh #768 §13.14. |
| Backlog sync | Close rows: [`get-completions-filtertext-doesnt-promote-in-scope-members`]. Parent tracker gh #768 §13.14 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **CRITICAL — Roslyn API correction:** the plan cites `CompletionTrigger.TriggerOnInsertion(char)` — that overload does **NOT exist** in the referenced Roslyn package. Use `Microsoft.CodeAnalysis.Completion.CompletionTrigger.CreateInsertionTrigger(triggerCharacter.Value)` at `CompletionService.cs:53`. Adjacent factories: `CreateDeletionTrigger(char)`, `CompletionTrigger.Invoke`, `CompletionTrigger.Default`.
- **Verified fanout (Rule 5b probe — addressing reviewer warn):** `ICompletionService.GetCompletionsAsync` has exactly **1 production call site (`SymbolTools.cs:767`) + 1 implementation (`CompletionService.cs:9`) + 2 test call sites in `ServiceCoverageTests.cs:220` and `:237`** that also need the new `triggerCharacter` argument threaded through. DI registration at `ServiceCollectionExtensions.cs:68` unaffected.
- **Pattern coordinates:** the tool wrapper edit lands in `SymbolTools.cs:749-770` — the `GetCompletions` `[McpServerTool]` definition. Insert the new `[Description] char? triggerCharacter = null` parameter between `maxItems` (line 761) and `CancellationToken ct` (line 762), then thread it into the call at line 767.
- **Test target:** extend `ServiceCoverageTests.cs` (existing class, line 212 region `// ── CompletionService ──`). Mirror the existing fixture `GetCompletions_Ranking_BoostsInScopeBeforeExternalTypes` at lines 227-269 — same `AnimalService.cs` fixture, same column 30, but pass `triggerCharacter: '.'`. Update the two existing call sites at lines 220-221 and 237-238 to pass `triggerCharacter: null`.
- **Hotspot seam:** `SymbolTools.cs` is 901 LOC. The edit is bounded to the `GetCompletions` method at lines 749-770 only; do NOT touch sibling tools `GoToTypeDefinition` (lines 726-747) or `ParseProjectFilter` (line 780).
- **Negative space:** do NOT modify `CompletionService.cs:37` (`Microsoft.CodeAnalysis.Completion.CompletionService.GetService(document)` — that's the Roslyn factory, not the project's `ICompletionService`). Do NOT add a non-nullable overload "for safety" — the nullable `char?` with conditional pass-through is the correct single path.

</details>

### 8. semantic-grep-dotted-identifiers-tokenization-docs-gap

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `semantic-grep-dotted-identifiers-tokenization-docs-gap` |
| Diagnosis | The `semantic_grep` tool description at `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:430` hints at per-token matching but never explains the consequence for dotted member-access expressions. Root cause in `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs:154`: `scope="identifiers"` walks `SyntaxKind.IdentifierToken` one at a time — `Task.Run` tokenizes as `Task`/`./`/`Run` so `pattern="Task.Run"` matches 0. Backlog anchor `src/RoslynMcp.Host.Stdio/Tools/SemanticGrepTools.cs` is stale — actual host file is `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. |
| Approach | Edit the `[Description]` attribute on `AnalysisTools.SemanticGrep` (~line 430) — append a tokenization-rule paragraph: (1) name the `identifiers` scope's one-token-per-match semantic, (2) give concrete failing example (`pattern="Task.Run"` matches 0), (3) recommend `scope="all"` for prose-style queries or two separate calls + line intersection. Optional `dottedIdentifier=true` mode deferred. Extend `SemanticGrepServiceTests.cs` with `SemanticGrep_DottedPattern_IdentifierScope_ReturnsZero` and a companion `scope="all"` assertion. |
| Scope | Production files (1, tool-surface-only exemption): `AnalysisTools.cs`. Test files (1): `SemanticGrepServiceTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | (1) Description attribute is one long string literal — `compile_check` after edit. (2) Test fixture must have an actual `Task.Run` call in the shared sample workspace. (3) `scope="all"` matches comments/strings as a trade-off worth documenting. (4) Stale anchor noted. (5) Optional `dottedIdentifier=true` mode is deferred. |
| Validation | `compile_check`; new test passes; `mcp__roslyn__test_run --filter SemanticGrepServiceTests`; `eng/verify-ai-docs.ps1` clean. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `semantic_grep` tool description to explicitly document that the `identifiers` scope tokenizes on C# lexer boundaries: dotted member-access expressions (e.g. `Task.Run`) are multiple tokens and will not match as a single pattern. Recommended multi-token strategies (`scope="all"` with prose-fragment, or separate per-identifier calls) are now documented inline. Added regression test verifying zero hits for dotted patterns in identifier scope. Fixes gh #768 §13.15. |
| Backlog sync | Close rows: [`semantic-grep-dotted-identifiers-tokenization-docs-gap`]. Parent tracker gh #768 §13.15 — do NOT auto-close. |

### 9. get-di-registrations-multi-registration-overcounting

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-di-registrations-multi-registration-overcounting` |
| Diagnosis | Two bugs in `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs`. **Bug 1:** `BuildOverrideChains` (line 380) groups all registrations for a service type and marks all-but-last as `overridden` — no `IEnumerable<T>` consumption awareness. 8× `AddSingleton<IAnalyzer,...>` reports `deadRegistrationCount: 7` though MS.DI's `GetServices<T>()` returns all. **Bug 2:** `TryCreateDiRegistration` (lines 217-224) detects lambda registration and unconditionally returns `implType = "factory"` — for `AddSingleton<ISnapshotReader>(sp => sp.GetRequiredService<FileSnapshotReader>())` the returned impl type falls back to the interface itself (line 234). |
| Approach | All changes in `DiRegistrationService.cs`. **Bug 1 fix:** extend `ScanProjectsAsync` to collect `IEnumerable<T>`/`IReadOnlyList<T>`/`IList<T>`/`T[]` constructor/property injection patterns. Thread the resulting "enumerable-consumed" set through `ScanSnapshot` (preserving reference-equality caching contract). In `BuildOverrideChains`, skip override-classification for service types in that set. **Bug 2 fix:** add `TryResolveLambdaReturnType` helper that walks the lambda body for `GetRequiredService<T>()` / `GetService<T>()` invocations and resolves the generic type via `semanticModel.GetTypeInfo`; fall back to `"factory"` if not found. Extend `tests/RoslynMcp.Tests/DiLifetimeOverrideTests.cs` (+ test-shim update) with 2 new methods. |
| Scope | Production files (1): `DiRegistrationService.cs`. Test files (1 extended): `DiLifetimeOverrideTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | (1) `IEnumerable<T>` detection: inline-during-scan vs second-pass — pick one; preserve reference-equality cache contract. (2) Test shim must add single-type-arg `AddSingleton<TService>(Func<IServiceProvider, TService>)` overload. (3) Also cover `IReadOnlyList<T>` and arrays for completeness. (4) Existing `Top10V2RegressionTests.GetDiRegistrations_RepeatCallSameVersion_ReturnsCachedReference` asserts reference equality on repeat calls — must preserve. |
| Validation | `compile_check`; targeted test runs; new tests assert (a) `deadRegistrationCount == 0` for `IEnumerable<T>`-consumed service; (b) impl type matches inner `GetRequiredService<T>` target. |
| Performance review | N/A — additional `IEnumerable<T>` scan runs once per cache miss. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `get_di_registrations` reporting false dead-registration counts for intentional multi-registration patterns (`IEnumerable<T>` consumers): service types injected as `IEnumerable<T>` are now excluded from the dead-count. Also fixed factory-lambda implementation-type resolution: `AddSingleton<IFoo>(sp => sp.GetRequiredService<FooImpl>())` now reports `FooImpl` as winning impl type. Fixes gh #768 §13.16. |
| Backlog sync | Close rows: [`get-di-registrations-multi-registration-overcounting`]. Parent tracker gh #768 §13.16 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates (Bug 2 lambda resolution):** mirror the existing `ResolveInstanceArgumentType` helper at `DiRegistrationService.cs:345-351` — same signature shape (`ExpressionSyntax`, `SemanticModel`, `CancellationToken`) and same `semanticModel.GetTypeInfo(...).Type.ToDisplayString()` pattern. Insert the new `TryResolveLambdaReturnType` call at line 222 inside the existing `args[0].Expression is AnonymousFunctionExpressionSyntax or LambdaExpressionSyntax` branch; fall back to `"factory"` only when resolution misses.
- **Hotspot seam (Bug 1 enumerable consumption):** `BuildOverrideChains` is `private static` (`DiRegistrationService.cs:380-460`) and has NO access to the solution — you must thread the `IEnumerable<T>`-consumed set through `ScanProjectsAsync` (line 138) → `ScanSnapshot` constructor (line 50) and feed it into `BuildOverrideChains` (change signature). Skip-classification belongs at line 388 (before the `groups` loop) or line 395 (per-group early-`continue`).
- **Cache contract (load-bearing):** `ScanSnapshot` (line 46) and `GetOrLoadSnapshotAsync` (line 98) must continue to return the same `LegacyView` reference on repeat calls — do NOT rebuild `LegacyView` per call. The enumerable-consumed set should live on the snapshot alongside `Raw`/`LegacyView`, computed once at line 124-127.
- **Test target:** extend `DiLifetimeOverrideTests.cs` (existing class, line 16). Mirror `Last_Wins_Add_Then_Add_Marks_Earlier_Singleton_As_Overridden_With_Scoped_Winner` at line 53. Test shim is the `WriteServiceCollectionShimAsync` helper at line 294 — add the single-type-arg factory overload `AddSingleton<TService>(this IServiceCollection, Func<IServiceProvider, TService>)` and a `Func<,>`/`IServiceProvider` stub to the shim block (lines 296-329).
- **Edge cases:** (1) `IEnumerable<T>` consumed via ctor injection; (2) `IReadOnlyList<T>` and `T[]` variants; (3) lambda body using BOTH `GetRequiredService<T>()` AND `GetService<T>()`; (4) lambda whose body has no recognizable `GetRequiredService<T>` call → fall back to `"factory"`, not throw.
- **Negative space:** do NOT touch `MapDiLifetime` (line 256), `IsTryAddMethod` (line 275), or `TryGetDiTypesFromTypeOfArguments` (line 284) — those are the FLAG-11A typeof-resolution and TryAdd-classification paths, separate from the two bugs in scope. Do NOT modify `GetDiRegistrationsAsync` (line 84) — returns the cached `LegacyView` directly and the cache contract must remain reference-equal.

</details>

### 10. migrate-package-preview-no-op-silent-mutation

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `migrate-package-preview-no-op-silent-mutation` |
| Diagnosis | Root cause in `src/RoslynMcp.Roslyn/Services/PackageMigrationOrchestrator.cs`. `BuildCentralVersionEditAsync` (called at lines 46-50 of `PreviewMigratePackageAsync`) is invoked unconditionally whenever CPM is enabled — regardless of whether any project actually referenced `oldPackageId`. Inside, `UpsertCentralPackageVersion` (lines 174-191) always upserts the new CPM entry. When zero projects reference `oldPackageId`, the per-project loop contributes 0 mutations but `BuildCentralVersionEditAsync` adds 1 mutation; the `mutations.Count == 0` guard at line 52 passes (count is 1) and silently mutates the package manifest. **Stale anchor:** backlog cited `MigratePackageService.cs` — does not exist; actual file is `PackageMigrationOrchestrator.cs`. |
| Approach | Single guard: in `PreviewMigratePackageAsync`, gate the CPM update block (lines 46-50) on `mutations.Count > 0` — only invoke `BuildCentralVersionEditAsync` when at least one project was migrated. The existing `InvalidOperationException` at line 52 (count == 0) then fires for the no-op case, which is the correct failure mode. No interface or DTO changes. New test in `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs`: `Migrate_Package_Preview_Throws_When_Source_Package_Absent`. |
| Scope | Production files (1): `PackageMigrationOrchestrator.cs`. Test files (1 extended): `OrchestrationIntegrationTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | (1) Edge: oldPackageId has CPM entry but zero project refs — CPM stays unchanged. Documented separately. (2) Verify existing `Migrate_Package_Preview_And_Apply_Updates_Project_Files_And_Central_Package_Versions` still passes. (3) Stale anchor noted. |
| Validation | New test asserts `Assert.ThrowsExceptionAsync<InvalidOperationException>` and `Directory.Packages.props` unmodified; existing happy-path tests pass; `compile_check`. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `migrate_package_preview` silently adding a `<PackageVersion>` entry to `Directory.Packages.props` for the replacement package when the source package has no references in any project file. The preview now throws with "No project references to '...' were found", leaving `Directory.Packages.props` unmodified. Fixes gh #768 §13.17. |
| Backlog sync | Close rows: [`migrate-package-preview-no-op-silent-mutation`]. Parent tracker gh #768 §13.17 — do NOT auto-close. |

### 11. scaffold-first-test-file-preview-single-target-heuristic

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `scaffold-first-test-file-preview-single-target-heuristic` |
| Diagnosis | Root cause: `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs:406-428` (`ResolveDestinationTestProject`). When `candidates.Count > 1` it immediately throws — no name-suffix tiebreaker. **Stale anchor:** backlog cited `ScaffoldFirstTestFileService.cs` — does not exist; code lives in the partial class `ScaffoldingService.TestBatchAndFirstTestPreview.cs`. `ResolveDestinationTestProject` is called once (line 250 same file) — fully self-contained. |
| Approach | In `ResolveDestinationTestProject` (lines 418-423), after the `candidates.Count > 1` branch: filter to names equal to `sourceProject.Name + ".Tests"` (`StringComparison.Ordinal`). If filtered set has exactly 1 member, treat as winner; otherwise fall through to existing error (updated to suggest explicit `testProjectName`). Extend `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs` with `FirstTestFile_InfersSuffix_When_MultipleTestProjects_Reference_Same_Library`. |
| Scope | Production files (1): `ScaffoldingService.TestBatchAndFirstTestPreview.cs`. Test files (1 extended): `ScaffoldingFirstTestFileTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) Solutions using `MyLib.UnitTests` or `MyLib.IntegrationTests` won't match — still require explicit `testProjectName` (intentional). (2) Test fixture must add second test project to isolated workspace without widening `IsolatedWorkspaceTestBase`. (3) Stale anchor noted. |
| Validation | `compile_check`; new test passes; existing single-candidate test still passes; ambiguous-after-tiebreaker case still surfaces clear error. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `scaffold_first_test_file_preview` failing with "Multiple test projects reference" when several test projects transitively reference a domain library but only one follows the `<Library>.Tests` naming convention. The service now applies a name-suffix tiebreaker and selects the unambiguous candidate automatically. Fixes gh #768 §13.18. |
| Backlog sync | Close rows: [`scaffold-first-test-file-preview-single-target-heuristic`]. Parent tracker gh #768 §13.18 — do NOT auto-close. |

### 12. remove-preview-family-invalidoperation-for-missing-items

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `remove-preview-family-invalidoperation-for-missing-items` |
| Diagnosis | All 4 absent-item throws are in `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`: line 109 (`PreviewRemovePackageReferenceAsync`), line 165 (`PreviewRemoveProjectReferenceAsync`), lines 330/354/370 (`PreviewRemoveTargetFrameworkAsync` — 3 branches), line 465 (`PreviewRemoveCentralPackageVersionAsync`). The "not found" check is inside the mutator lambda; when it throws, `PreviewXmlFileMutationAsync` propagates the exception. Correct LSP-aligned shape is empty preview (`Changes = []`, `PreviewToken = string.Empty`) — pattern already established by `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:94-106`. |
| Approach | All changes in `ProjectMutationService.cs`. For each of the 4 remove methods, change absent-item `throw InvalidOperationException` branches to return early with `RefactoringPreviewDto(string.Empty, "No changes — '<item>' was not found.", Array.Empty<FileChangeDto>(), null)` mirroring `StringLiteralReplaceService.cs:94-106`. For `PreviewRemoveTargetFrameworkAsync` the 3 branches all need treatment; promote the not-found guard before the inner `PreviewProjectMutationAsync` call. 4 new test methods in `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs` (one per tool). |
| Scope | Production files (1): `ProjectMutationService.cs`. Test files (1 extended): `ProjectMutationIntegrationTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) `PreviewRemoveTargetFrameworkAsync` has 3 distinct absent-item branches — partial fix would leave MSBuild-implied path still throwing. (2) Empty `PreviewToken` reaching `apply_project_mutation` → existing "invalid token" structured error (safe). (3) Happy-path remove tests must continue passing. (4) **Conflict:** shares `ProjectMutationService.cs` with initiative #6 — Phase C will mark this as a conflict edge. |
| Validation | 4 new tests assert `preview.Changes.Count == 0` and `preview.PreviewToken == string.Empty`; existing happy-path tests pass; `compile_check`. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `remove_package_reference_preview`, `remove_project_reference_preview`, `remove_target_framework_preview`, and `remove_central_package_version_preview` now return an empty preview (`changes: []`) when the specified item is not present, instead of throwing `InvalidOperationException`. Shape-probing callers can detect no-ops without exception-handling. Fixes gh #769 §13.29. |
| Backlog sync | Close rows: [`remove-preview-family-invalidoperation-for-missing-items`]. Parent tracker gh #769 §13.29 — do NOT auto-close. |

### 13. document-symbols-vs-symbol-info-record-kind-disagreement

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `document-symbols-vs-symbol-info-record-kind-disagreement` |
| Diagnosis | Root cause: `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs:245-250` (`GetKind`). For an `INamedTypeSymbol`, returns `namedType.TypeKind.ToString()`. For a positional record class, `TypeKind == TypeKind.Class` so `ToString()` yields `"Class"`. Meanwhile `document_symbols` uses `CollectSymbols` in `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:311` which checks `RecordDeclarationSyntax` and emits `"Record"`. **Stale anchors:** backlog cited `SymbolInfoService.cs` and `DocumentSymbolsService.cs` — neither exists; actual implementations are `SymbolSearchService.cs` and `SymbolMapper.cs`. |
| Approach | 1. In `SymbolMapper.cs` `GetKind` (~lines 245-250): three-arm switch: `IsRecord && TypeKind.Class → "Record"`, `IsRecord && TypeKind.Struct → "RecordStruct"`, else `TypeKind.ToString()`. 2. In `SymbolSearchService.cs` `CollectMembers` (lines 403-409): add `ClassOrStructKeyword` check to nested `RecordDeclarationSyntax` matching the pattern in `CollectSymbols` at line 311. 3. Extend `tests/RoslynMcp.Tests/SymbolMapperTests.cs` with 2 new methods using `AdhocWorkspace` (record class → `"Record"`, record struct → `"RecordStruct"`). |
| Scope | Production files (2): `SymbolMapper.cs`, `SymbolSearchService.cs`. Test files (1 extended): `SymbolMapperTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | (1) Callers switching on `"Class"` for records will now see `"Record"` — intended break. (2) `symbol_search.kind=Record` filter unchanged (already two-level check). (3) Secondary `CollectMembers` fix is bonus; can drop if scope challenged. (4) Stale anchors noted. |
| Validation | New `ToDto_RecordClass_KindIsRecord` + `ToDto_RecordStruct_KindIsRecordStruct`; `document_symbols` on same files returns matching kind; `compile_check`. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `symbol_info` returning `kind="Class"` for positional record types where `document_symbols` correctly returned `kind="Record"`. Both tools now use `"Record"` for record classes and `"RecordStruct"` for record structs. Fixes gh #769 §13.20. |
| Backlog sync | Close rows: [`document-symbols-vs-symbol-info-record-kind-disagreement`]. Parent tracker gh #769 §13.20 — do NOT auto-close. |

<details>
<summary>Sonnet handoff notes</summary>

**Sonnet handoff:**

- **Pattern coordinates:** mirror `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:311` — the top-level `CollectSymbols` switch uses `RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "RecordStruct" : "Record"`. The nested `CollectMembers` block at `SymbolSearchService.cs:402-410` currently has the buggy `RecordDeclarationSyntax => "Record"` arm — replace with the same `ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "RecordStruct" : "Record"` expression, binding the syntax node (`RecordDeclarationSyntax r =>`).
- **SymbolMapper edit:** `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs:245-250` — extend the `INamedTypeSymbol` arm into a nested switch on `IsRecord` + `TypeKind` (record class → `"Record"`, record struct → `"RecordStruct"`, else `namedType.TypeKind.ToString()`).
- **Test target (correction):** add 2 `[TestMethod]` fixtures to existing `tests/RoslynMcp.Tests/SymbolMapperTests.cs` class (line 13); do NOT create a new file. Plan says "AdhocWorkspace" but existing siblings (`ToDto_Interface_MapsKindAndFqName` line 28, `ToDto_Class_MapsKindAndHierarchy` line 42) use `SharedWorkspaceTestBase` + `SampleLib` metadata names — prefer that pattern (add `record` types to `samples/SampleSolution/SampleLib`) unless hermetic isolation is required (then `AdhocWorkspace` is acceptable).
- **Edge cases:** (1) positional record class → `"Record"`; (2) `record struct` → `"RecordStruct"`; (3) confirm `document_symbols` and `symbol_info` agree on identical source for both shapes.
- **Negative space:** do NOT touch `SymbolSearchService.cs:305-313` (top-level `CollectSymbols` switch — already correct). Do NOT modify `symbol_search.kind` filter logic (already two-level).

</details>

### 14. get-msbuild-properties-vs-workspace-reload-outputtype-mismatch

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-msbuild-properties-vs-workspace-reload-outputtype-mismatch` |
| Diagnosis | The production-side mismatch has already been resolved by a prior `project-graph-output-type-misreports-sdk-defaulted-exe` initiative. `WorkspaceManager.cs:2120` calls `ProjectMetadataParser.GetOutputType` (the MSBuild-evaluated overload) which returns `Exe` for SDK.Web projects. `MsBuildEvaluationService.GetEvaluatedPropertiesAsync` (~line 82) independently runs `ProjectCollection.LoadProject` — also returns `Exe`. Both surfaces draw from the same evaluation path. **Gap:** no test asserts cross-surface consistency. **Stale anchor:** backlog cited `MsBuildPropertyService.cs` — does not exist; correct file is `MsBuildEvaluationService.cs`. |
| Approach | **Test-only initiative.** Add `BothSurfaces_SdkWebProject_AgreeonOutputType_Exe` to `tests/RoslynMcp.Tests/ProjectOutputTypeTests.cs`: create a `Microsoft.NET.Sdk.Web` `.csproj` fixture, load via `WorkspaceManager.LoadIntoSessionAsync`, read `ProjectStatusDto.OutputType`, also call `MsBuildEvaluationService.GetEvaluatedPropertiesAsync` with `includedNames: ["OutputType"]`, assert both return `"Exe"`. No production-file changes. |
| Scope | Production files (0). Test files (1 extended): `ProjectOutputTypeTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | Loading a full workspace in a test is heavier than existing unit tests — use `TestBase` infrastructure already in place. Stale anchor noted. |
| Validation | `mcp__roslyn__test_run --filter ProjectOutputTypeTests` (existing 5 + new pass); `compile_check`. |
| Performance review | N/A. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed: `get_msbuild_properties` and `workspace_reload` now agree on `OutputType` for `Microsoft.NET.Sdk.Web` projects — both return `Exe`. Added cross-surface regression test guarding against future divergence. Fixes gh #769 §13.25. |
| Backlog sync | Close rows: [`get-msbuild-properties-vs-workspace-reload-outputtype-mismatch`]. Parent tracker gh #769 §13.25 — do NOT auto-close. |

### 15. source-file-lines-off-by-one-marker-count

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `source-file-lines-off-by-one-marker-count` |
| Diagnosis | Root cause: `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:285`. `GetSourceText` computes `totalLineCount` as `text.Count(ch => ch == '\n') + 1` — does NOT apply the trailing-newline correction. For files ending with `\n` this returns N+1. The `source_file_lines` resource at `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs:255` correctly delegates to `RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(text)` (which subtracts 1 when last char is `\n`). **Stale anchors:** backlog cited `SourceFileLinesResource.cs` and `SourceTextService.cs` — neither exists; actual files are `WorkspaceResources.cs` and `WorkspaceTools.cs`. |
| Approach | One-line replacement at `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:285`: replace `var totalLineCount = text.Count(ch => ch == '\n') + 1;` with `var totalLineCount = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(text);`. Extend `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs` with `WorkspaceTools_GetSourceText_TotalLineCount_MatchesResourceMarker`. |
| Scope | Production files (1): `WorkspaceTools.cs`. Test files (1 extended): `WorkspaceToolsIntegrationTests.cs`. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | (1) Callers hard-coding old N+1 will see -1 change — internal-only callers verified. (2) Existing tests pass (`>= 1` assertion); clamp logic uses corrected value. (3) Fanout skipped — one-expression replacement. (4) Stale anchors noted. |
| Validation | New test passes (matches resource marker N); `Top10V2RegressionTests` + `WorkspaceToolsIntegrationTests` pass; `EndPastEof_ClampsToFileEnd` still passes. |
| Performance review | N/A — `SourceTextSlicer.CountLines` is O(N) single-pass, same cost as inline LINQ. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `get_source_text` reporting `totalLineCount` one higher than the `source_file_lines` resource marker for files ending with a newline; both surfaces now use `SourceTextSlicer.CountLines` so the counts are consistent. Fixes gh #769 §13.26. |
| Backlog sync | Close rows: [`source-file-lines-off-by-one-marker-count`]. Parent tracker gh #769 §13.26 — do NOT auto-close. |

## Conflict graph

**1 edge:** (#6, #12) on `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`.

- #6 (`set-conditional-property-preview-allowlist-narrowness`) edits the `AllowedProperties` HashSet (~lines 17-23) and the `ValidateAllowedProperty` reference.
- #12 (`remove-preview-family-invalidoperation-for-missing-items`) converts 4 absent-item throws to empty-preview returns in `PreviewRemovePackageReferenceAsync` (line 109), `PreviewRemoveProjectReferenceAsync` (line 165), `PreviewRemoveTargetFrameworkAsync` (lines 330/354/370 — three branches), `PreviewRemoveCentralPackageVersionAsync` (line 465).

The edits are in **disjoint methods/regions** within the same file; they would not produce a Git merge conflict but the executor SHOULD schedule them in different parallel waves to avoid serialization friction during the merge.

**Zero-degree initiatives (13 of 15):** [1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 13, 14, 15] — safely parallelizable.

**Hotspot adjacency check:** None of the 15 initiatives touch the addenda-listed hotspots (`ServerSurfaceCatalog.cs`, `ServiceCollectionExtensions.cs`, `WorkspaceManager.cs`). The wave rule (≤1 hotspot per wave) does not bind.

**Suggested wave plan** (parallel mode, 4 inits/wave):
- Wave 1: 1, 2, 3, 4
- Wave 2: 5, 6, 7, 8 (includes #6 ProjectMutationService.cs touch)
- Wave 3: 9, 10, 11, 12 (includes #12 ProjectMutationService.cs touch — separated from #6 by one wave)
- Wave 4: 13, 14, 15

Or compact 5/5/5 split keeping #6 and #12 in different waves.

## Review

**Cycle 0 outcome: passed-with-warnings** (0 block, 1 warn, 0 info). See `review-cycle-0.md` (or `review.md`) for the full reviewer artifact.

Key signal:
- **Warn (Rule 5b):** Initiative #7 (`get-completions-filtertext-doesnt-promote-in-scope-members`) — `ICompletionService` interface signature change with `fanoutEstimate: null`. Reviewer-verified the actual fanout is bounded (1 impl + 1 call site, matching the plan's `productionFilesTouched: 3`). Non-blocking; executor should re-verify the impl count before applying.
- No info findings.

No remediation cycle required (`block: 0`).
