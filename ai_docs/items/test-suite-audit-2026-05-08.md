# Test suite audit — 2026-05-08

<!-- purpose: Concrete findings from a Roslyn-MCP-tools-driven audit of `tests/RoslynMcp.Tests/`. Surfaces duplicated test-helper code, candidate parameterizations, and patterns worth investigating. Output of the "Path A — quick analysis pass" decision. -->
<!-- scope: in-repo -->
<!-- status: findings; backlog rows TBD by maintainer triage -->

**Generated:** 2026-05-08
**Tools used:** `mcp__roslyn__find_duplicated_methods` (minLines=8), `mcp__roslyn__get_complexity_metrics` (minComplexity=10), targeted Grep for sleep/skip patterns.
**Scope:** Project `RoslynMcp.Tests` only.

---

## Bottom line

The test suite is in **better shape than feared** in some dimensions and **clearly improvable** in others.

- **Test-body complexity is low.** Only 3 methods above complexity-10 (out of ~1130 tests); all 3 are legitimate parametric assertion shapes. **Not a problem area.**
- **No `[Ignore]`d or skipped tests.** Clean.
- **Real duplication exists in test-helper code, not test bodies.** Copy-pasted utility methods across many files — the kind of accidental drift that happens when each test class adds its own filesystem-cleanup helper rather than reaching for a shared one.
- **Some tests are candidates for parameterization** (`[DataRow]`/`TestSource`) where the same assertion shape repeats with different inputs. Real opportunities; not all are wins.

CI speedup from these refactors would be **incidental** (smaller compile target, slightly less work per fixture). The bigger wins are **maintenance burden** (14 implementations of "delete dir with retry" = 13 places to forget to update if Windows file-locking semantics change) and **test clarity** (extracting boilerplate makes the actual assertion more visible).

If the goal is purely CI wall-clock, this audit's wins won't move the needle materially. If the goal is honest test-suite hygiene, the wins are concrete and worth pursuing.

---

## Highest-value finding: `TryDeleteDirectory` copy-pasted 13 times

**Severity:** High value, low risk.

13 test files contain an identical 14-line `TryDeleteDirectory` helper that retries directory deletion against Windows file-locking. Bodies are AST-identical (the duplicate-methods tool requires exact normalized-hash match).

**Files:**
- `BulkRefactoringTests.cs:155`
- `CsprojReserializationTests.cs:317`
- `ExtractInterfaceSemanticUsingsTests.cs:408`
- `ExtractMethodFormatRegressionTests.cs:263`
- `ExtractMethodThisExclusionTests.cs:150`
- `MoveTypeDiskStateTests.cs:102`
- `RenameSummaryModeTests.cs:147`
- `ReplaceInvocationTests.cs:236`
- `SdkStyleCsprojInjectionTests.cs:164`
- `Services/WorkspaceCacheStoreInvalidationTests.cs:32` (named `Teardown`, same shape)
- `Services/WorkspaceCacheStoreRoundTripTests.cs:29` (named `Teardown`, same shape)
- `TypeExtractionTests.cs:348`
- `UndoFileOperationsTests.cs:115`
- *(plus `MutationAnalysisSideEffectsTests.cs:127`'s `ClassCleanup` shares the shape per a separate cluster)*

**Recommendation:** Extract to `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs`. That helper already exists — just hasn't been used here. Single static method `TryDeleteDirectory(string)` with the existing retry semantics; 13 call-sites become `TestFixtureFileSystem.TryDeleteDirectory(path)`.

**Net:** delete ~180 lines of duplicate body, replace with 13 one-liner call-sites + 1 `using` per file. Roughly **−150 lines, +0 risk** (every site already calls the same logic).

**Sized as a backlog row:** ~4-6 production files per Rule 3 limit means split across **3 child rows**, ~5 files each. Or one larger row if the maintainer accepts the heroic-touch (it's mechanical and reviewable).

---

## Second tier: 5 duplicate clusters worth lifting

### `FindDocumentPath` — 6 copies (identical 9-line bodies)

Files: `ExpandedSurfaceIntegrationTests.cs:626`, `PromptSmokeTests.cs:211`, `RefactoringToolsIntegrationTests.cs:268`, `ValidateWorkspaceSummaryTests.cs:129`, `ValidationToolsIntegrationTests.cs:367`, `WorkspaceResourceTests.cs:154`.

**Recommendation:** Extract to `TestFixtureFileSystem` or a similar test-helper. ~50 lines deleted, 6 call-sites updated.

### `AddProjectToCopiedSolution` — 2 copies (13-line bodies)

`CrossProjectRefactoringIntegrationTests.cs:313` and `OrchestrationIntegrationTests.cs:409`. Smaller win; extract to a shared helper. ~13 lines delete + 1 call-site update.

### `CreateSymbolRefactorService` — 2 copies (12-line bodies)

`CompositeSplitServiceDiPreviewTests.cs:134` and `RecordFieldAddSatelliteTests.cs:279`. DI-construction helper. Extract to test infrastructure. ~12 lines delete.

### `ClassCleanup` boilerplate — 3 sites with identical 10-line shape

`FindPropertyWritesPositionalRecordTests.cs:61`, `FlowAnalysisServiceTests.cs:45`, `MutationAnalysisSideEffectsTests.cs:127`. **Probably keep separate** — `ClassCleanup` is fixture-lifecycle scaffolding and tight-coupling test classes via a base just to share `ClassCleanup` is anti-pattern. Flag, don't extract.

### `ClassInit` boilerplate — 2-3 sites

`ChangeSignaturePreviewMetadataNameShapeTests.cs:24` + `ChangeSignaturePreviewTests.cs:25` (9-line); `GoToTypeDefinitionTests.cs:17` + `PositionProbeTests.cs:15` (8-line). Same advice as `ClassCleanup` — keep separate.

---

## Third tier: parameterization candidates (medium effort, modest wins)

### `SliceFieldDetectionTests` — 4 tests with 47-52 line bodies, near-identical structure

`ServerSurfaceCatalogAnalyzerTests.cs:59` shares the 52-line shape with three `SliceFieldDetectionTests.cs` methods (lines 40, 82, 131). Each test calls the same analyzer-verification flow with different setup.

**Recommendation:** Convert to `[DataRow]`-parameterized test if MSTest fixture-shape allows. Caveat: each row would still rerun `[ClassInit]`-level setup, so this is a *clarity* refactor not a *speed* refactor. Worth doing only if the test names + assertion messages make sense as one parameterized template.

### `StdoutWriteAnalyzerTests` — 4 tests with 33-line bodies

`StdoutWriteAnalyzerTests.cs:32, 161, 184, 207`. Also looks like analyzer-verification with different inputs (FlushAndStderrWrites, ConsoleErrorWriteCalled, ConsoleOutFlushCalled, StderrAliasWriteCalled). Same parameterization opportunity.

### `NavigationToolsNotFoundMessageTests` + `SymbolInfoNotFoundMessageTests` — 4 NotFound tests with 21-line shape

`NavigationToolsNotFoundMessageTests.cs:38, 65, 95` + `SymbolInfoNotFoundMessageTests.cs:36`. **Probably keep separate** — testing different tool entry points (CallersCallees vs ImpactAnalysis vs FindConsumers vs SymbolInfo), and the test names encode which tool failed. Parameterizing would obscure which entry point regressed.

### `SdkStyleCsprojInjectionTests` — 3 tests with 12-line bodies

`SdkStyleCsprojInjectionTests.cs:31, 44, 73`. Three boolean-property assertions (AttributeForm, ElementForm, LegacyNonSdk). Could parameterize, but the test names tell you which form failed, which is useful. Probably keep separate.

---

## What I looked for but did NOT find

- **High-complexity tests.** Only 3 methods above complexity-10 across the whole project (lines: `ServiceCoverageTests.cs:191`, `ReadmeSurfaceCountTests.cs:97`, `SurfaceCatalogTests.cs:82`). All 3 are legitimately parametric — keep them.
- **Skipped/`[Ignore]`d tests.** Zero. The suite is honestly run.
- **Hardcoded `Thread.Sleep` patterns** in test bodies (as opposed to test infrastructure). 10 files use `Thread.Sleep`/`Task.Delay`, but spot-check suggests they're in legitimate places: `WorkspaceExecutionGateTests.cs` (concurrency-gate timing), `ValidateRecentGitChangesTests.cs` (timeout behavior), `ExternalEditStalenessTests.cs` (file-system staleness propagation), benchmark/race-test scenarios. Worth a one-pass review but unlikely to surface wins.

These are areas where I expected to find something and didn't — useful negative evidence.

---

## Patterns the audit can't surface

These are real opportunities the duplicate-methods + complexity tools don't catch. Worth a separate effort if test speed is the goal:

1. **Fixture-load overhead per test class.** If tests load workspaces or build solutions in `[ClassInit]` rather than `[AssemblyInitialize]`, every test class pays the load cost. Hard to detect statically; a per-class profile would surface it.
2. **Real `dotnet build` shell-outs.** The `Build_Workspace_Returns_Structured_Success` 5-min timeout flake is the marquee example. Look for tests that invoke MSBuild on real solutions; consider mocking MSBuild output for most cases and keeping one canary.
3. **Workspace-load-per-test.** If test methods (not just classes) load workspaces individually, every test pays multi-second setup. Audit by greping test methods for `WorkspaceManager.LoadAsync` or similar fixture-construction patterns.
4. **Tests that rerun the same sample-solution build.** If multiple integration tests each run the full SampleSolution build, that's a parallelization-of-waste rather than parallelization-of-progress.

These need a runtime profile (`dotnet test --logger "trx"`) or per-test timing hooks to surface honestly. Out of scope for this static-analysis pass.

---

## Recommended actions

**Tier 1 — clear wins (worth a backlog row):**

1. **`extract-test-tryDeleteDirectory-helper`** (Medium): consolidate 13-14 copies of `TryDeleteDirectory` into `TestFixtureFileSystem`. ~180 lines deleted, 13 call-sites updated. Touches **13+ test files**, so per Rule 3 (≤4 prod files) this is **3 child rows**:
   - 1a: `BulkRefactoringTests`, `CsprojReserializationTests`, `ExtractInterfaceSemanticUsingsTests`, `ExtractMethodFormatRegressionTests` (4 files)
   - 1b: `ExtractMethodThisExclusionTests`, `MoveTypeDiskStateTests`, `RenameSummaryModeTests`, `ReplaceInvocationTests`, `SdkStyleCsprojInjectionTests` (5 files — borderline; probably split further)
   - 1c: rest of the cluster + `Services/WorkspaceCacheStore*` + `TypeExtractionTests` + `UndoFileOperationsTests`
   - Plus the `TestFixtureFileSystem.cs` edit (1 file across all rows)

   Or if the maintainer accepts a heroic-touch row for purely-mechanical changes: 1 row that does all 13. Cite the heroic-touch exemption explicitly.

2. **`extract-test-findDocumentPath-helper`** (Medium): consolidate 6 copies of `FindDocumentPath`. 6 files; fits Rule 3 in 2 child rows or 1 heroic-touch row.

**Tier 2 — modest wins (defer or roll into the Tier 1 rows):**

3. **`extract-test-addProjectToCopiedSolution-helper`** + **`extract-test-createSymbolRefactorService-helper`** (Low): 2-call-site each. Could roll into Tier 1 rows when convenient or skip until the surrounding tests are touched for other reasons.

4. **Parameterize `SliceFieldDetectionTests` + `StdoutWriteAnalyzerTests`** (Medium-Low): clarity refactor, not a speed win. Only valuable if the maintainer wants to do it for readability. Defer unless someone needs it.

**Tier 3 — needs runtime profile (future row):**

5. **`profile-test-runtime-and-extract-slow-class`** (High): run `dotnet test --logger "trx"` on the suite, extract per-test timing, identify the top-20-slowest. Investigate each: is it doing genuine integration work or is it shell-out-MSBuild churn? Output: a follow-up audit doc with concrete speed-win candidates.

Tier 3 is the path that produces real CI speedup. Tier 1+2 produce maintainability wins with incidental speedup.

---

## How this changes the CI-sharding question

The audit doesn't kill the CI-sharding case but does shift the calculus:

- **If the suite is healthy structurally** (no big duplicates, no skip-noise, complexity is under control), CI sharding is the only remaining lever for wall-clock improvement.
- **The audit found duplicates in helpers, not test bodies.** Fixing the helpers makes the suite cleaner but doesn't shrink test count or dramatically reduce execution time.
- **The Tier 3 runtime profile is what would inform the sharding decision.** If 80% of test time is concentrated in 10 tests, sharding shouldn't separate them blindly — it should isolate the slow ones onto a dedicated shard so the rest finish fast.

**Order of operations I'd recommend:**
1. Ship the Tier 1 cleanups (small, mechanical, low-risk; concrete wins for maintainability).
2. Run Tier 3 (runtime profile) to get evidence-driven sharding.
3. *Then* decide whether to ship CI sharding, larger runners, or further test-level fixes.

The audit doesn't give an answer for #3 — it gives evidence to decide #3 honestly.
