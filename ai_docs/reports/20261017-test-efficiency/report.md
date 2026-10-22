# Repository-wide test and fixture efficiency audit

<!-- scope: in-repo -->

| Field | Evidence |
|---|---|
| Date | 2026-10-17 |
| Source head | `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7` |
| Change scope | Audit artifacts and backlog intake only; no product, test or CI behavior changed |
| Coverage | Every tracked file under tests/, samples/, root sample trees, eng/, workflows/, scripts/, plus solution/runner entry points: 564 files |
| Main test project | 442 C# files; 411 TestClass attributes; 3,369 TestMethod attributes; 1,482 DataRow attributes |
| Additional fixtures | Shard-discovery assembly, Git worker probe, four owned sample solutions, root duplicate sample tree |
| Method | Parse all 492 C# files with Roslyn; zero syntax errors; inspect all method bodies for exact token duplicates and all invocation/attribute nodes for setup, copies, loads, sleeps and serialization; deeply trace hotspot/helper/CI candidates |
| Limits | Structural coverage of every file is not a line-by-line semantic correctness review of every assertion. Counts are syntax counts, not discovered runtime cases. Existing compiled Roslyn libraries were used only to parse source. |
| Runtime evidence | Downloaded and parsed 21 TRX files from five recent successful PR runs; three complete code matrices, one two-leg docs suite, and one short docs artifact. No full suite or speedup experiment was run in this audit. |
| MCP | Repository documents Roslyn MCP; deferred catalog contains no Roslyn or heartbeat/server-info candidate. Shell/source fallback used. |

## Highest-value findings

| Rank | Finding | Evidence | Recommended change | Tracking |
|---|---|---|---|---|
| 1 | One huge class limits both MSTest concurrency and hosted class sharding | ChangeSignaturePreviewTests: 333 expanded cases; latest Windows 854.3s within a 971.7s leg; Linux 800.1s within 951.1s | Split by behavior into distinct TestClass types; preserve cases and shared helper reuse; establish independent preview-store/workspace ownership first | `test-change-signature-class-granularity` |
| 2 | Single-project refactoring setup loads a full three-project sample | ParameterObjectPreviewTests.SetupSingleProjectFixtureAsync:2478 copies SampleSolution and loads the solution; signature cases repeat the same pattern | Introduce a minimal real MSBuild project fixture for self-contained cases, retaining cross-project and real apply/emit tests | `test-single-project-refactoring-fixtures` |
| 3 | Expensive edit/cache/scaffold tests repeatedly copy and load the sample | 524 syntactic CreateIsolatedWorkspace* call sites across the corpus; includes helper forwarding, so not a runtime invocation count | Prefer minimal fresh fixtures; use class-private reset only where complete byte/version/cache/token/undo restoration is proven | Existing amortization waves 1-3; update their inherited design before implementation |
| 4 | Private restore-policy fixtures are serialized without a documented shared dependency | WorkspaceLoadAutoRestoreTests:16, WorkspaceLoadRestoreBudgetTests:17, WorkspaceLoadRestoreFailureWireTests:17 use private copies/managers and scripted execution | Re-derive reachable global state, then prove safe concurrency and remove opt-outs | `test-restore-fixture-serialization` |
| 5 | One shared build serializes an entire validation class | ValidationToolsIntegrationTests:9-18 identifies real build writes to canonical SampleLib obj/ while other classes copy it | Move build case to an owned fixture, then revalidate class concurrency | `test-validation-build-shared-fixture` |
| 6 | Two generator tests cite generic contention as serialization justification | DiagnosticSourceGeneratorParityTests:6-11 and GeneratorProjectSymbolIdentityTests:15-18 own their fixture roots | Investigate actual process/package ownership; remove serialization only after concurrent proof, not by assertion | `test-generator-fixture-parallel-ownership` |
| 7 | No-op lifecycle API obscures resource ownership | 153 ClassCleanup methods contain only DisposeServices(); TestBase:180-187 explicitly does nothing | Delete inert hooks in reviewable batches, then retire the API; retain assembly disposal and meaningful class cleanup | `test-noop-class-cleanup-retirement` |
| 8 | Private performance workspace outlives its deleted directory | DiagnosticServicePerfTests:36-51 loads a private workspace, deletes the root, calls no-op DisposeServices | Close owned workspace before deletion with failure-safe cleanup | `test-diagnostic-perf-fixture-close-before-delete` |
| 9 | Small genuine duplicates exist | Translator tests:24/230; unknown-workspace resource tests ErrorResponseObservabilityTests:134 and FetchMcpResourceReadinessTests:138 | Keep one exact input/entry-point/assertion regression per pair; preserve all different inputs and wire coverage | `test-tree-filter-duplicate-case`, `test-workspace-resource-duplicate-refusal` |
| 10 | Compilation fixture helper copied verbatim | SymbolTraversalTests:216 and TypeNamespaceWalkerTests:86 have identical complete token bodies | Share synchronous compilation construction; preserve reference/options identity | `test-symbol-compilation-helper-dedupe` |

## Measured hotspots

Latest complete code matrix: [CI run 38827311644](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/38827311644), source `242a9012c3117db56282003488a1d78d5a1144b7`.
`git diff <sample-head> HEAD -- tests eng .github/workflows` returned no changes; the latest timing sample's relevant source matches this audit.

| Class | Windows case sum (seconds) | Linux case sum (seconds) |
|---|---:|---:|
| ChangeSignaturePreviewTests | 854.3 | 800.1 |
| ParameterObjectPreviewTests | 350.7 | 266.9 |
| ProjectMutationIntegrationTests | 245.3 | 217.8 |
| EditUndoCohesionTests | 241.1 | 140.2 |
| ScaffoldingIntegrationTests | 229.8 | 145.7 |
| ExtractMethodTests | 193.0 | 115.6 |
| CompilationCacheAdoptionTests | 165.5 | 138.4 |
| ExtractSharedExpressionTests | 164.8 | 129.5 |
| FormatterBaselineContractTests | 148.9 | 200.8 |
| TypeExtractionTests | 137.6 | 98.3 |

- Latest Windows test-leg walls: 971.7, 471.9, 584.2, 591.6 seconds. Linux: 951.1 and 742.8 seconds.
- Three complete code runs: `38810577303`, `38818129051`, `38827311644`. Signature class Windows sum: 1123.0, 1281.2, 854.3 seconds; corresponding leg walls: 1222.3, 1369.9, 971.7 seconds.
- Docs suite `38815410834` supplies additional Linux corroboration. Short artifact `38809032189` is retained in raw tables but is not treated as a complete baseline.
- Class-level execution makes the signature class a long sequential work item. Its latest case sum is 88% of its Windows leg wall; this is not a promise of 88% savings.
- Case sums across concurrently executing classes are not additive wall time. No OS-specific duration profile or shard-count change is justified by this sample set; retain CI_POLICY.md's five-complete-run calibration requirement.

## Fixture and CI decisions

| Area | Disposition | Reason / acceptance condition |
|---|---|---|
| Pure DTO, parser, formatter, validation and narrow in-memory unit tests | Keep by default | Scan established no general case-deletion opportunity. Similar names or assertions alone do not imply duplicate coverage. |
| Shared read-only sample workspace | Keep synchronized sharing | WorkspaceIdCache already amortizes initial loads. Do not move mutators into this fixture. |
| Single-project mutation cases | Slim the project graph first | Preserves fresh state without adding a complex reset protocol; retain real compiler references and disk apply behavior. |
| Cross-project, reload, restore, eviction and lifecycle tests | Retain required isolated graphs | Fresh workspace identity and load behavior are part of the contract in many cases. Do not amortize away the behavior being tested. |
| Generator/analyzer fixtures | Retain real generator parity | Harness replacement with mocks would lose the defect class. Review only process ownership and unnecessary surrounding projects. |
| Protocol/wire harnesses | Adopt existing minimal-fixture and host-composition work | Preserve legacy/current negotiations, raw frame/error/notification distinctions and cleanup. |
| Fixture copying | No unsupported bulk-copy performance claim | Current local sample copy excluding bin is only 97 files / 0.75 MiB. Loading/compiling its project graph is a stronger lead than copying bytes. obj is copied today; never simply exclude it and trigger per-test restore or unresolved references. |
| Root sample tree | Consolidate existing duplicate rows | `root-sample-solution-duplicates-removal` and `root-sample-solution-tree-duplicate` describe the same stale tree. Verify consumers before deletion. Root AnimalServiceTests duplicates the owned sample test bodies. |
| Fixture preparation | Retain single pre-test restoration | verify-release prepares all samples once and passes TestFixturesPrepared=true; raw dotnet test uses the VSTest target. No duplicate restore was established on the normal release path. |
| Exact SDK floor | Keep separate compatibility probe | The floor lane's raw dotnet test invokes the fixture target after its explicit sample restore; narrowly prepared probe execution is a possible follow-up, not removal of exact-floor coverage. |
| Formatting integration | Design candidate requiring measurement | FormatterBaselineContractTests:660 invokes the full generator twice (first restore+format, then no-restore format). Latest deterministic case: 113.8s Windows / 166.0s Linux. Evaluate a small owned formatting fixture for deterministic-output/parser tests while retaining one real repository-wide baseline check and equivalent restore/no-restore subprocess contracts. Do not drop the second pass without replacing its determinism proof. |
| Subprocess timeout tests | Keep lifecycle assertions; decouple scheduling where feasible | PwshScriptRunnerTests inherited-pipe cases consume about 25s and 40s by construction (15/30s timeout plus 10s drain). A controlled clock/deadline seam must retain real native pipe inheritance and owned-child teardown. Tracked by timing-test-reaudit-wave. |
| Watcher tests | Replace guessed sleeps with observable delivery or controlled collaborator | EditorConfigServiceTests:225 waits 300ms before a negative assertion; ExternalEditStalenessTests has related waits. A sleep does not prove an event ran. Keep real watcher integration plus deterministic policy coverage. |
| Drift timestamp tests | Set explicit filesystem timestamps relative to LoadedAtUtc | Services/WorkspaceDriftServiceTests:103,174 sleeps 50ms to establish mtime order. Verify the timestamp written/read and preserve reload semantics. |
| Preview TTL test | Consider an injected clock in the existing store abstraction | PreviewStoreTests:288 uses a 1ms TTL and 30ms sleep. Small runtime benefit; never shorten sleep as the fix. |
| Class concurrency | No blanket method-level parallelism | Shared preview-store capacity, mutable helper services and per-class static fields need explicit ownership first. |
| 35 DoNotParallelize attributes | 28 class-level and 7 method-level | Static security/recycle publication, shared-workspace reload and genuine timing isolation remain valid reasons. Preserve WorkspaceLoadRestoreRaceTests' recorded concurrent failure unless the underlying timing mechanism is repaired. |
| Windows/Linux matrix and network/coverage lanes | Retain | Platform-sensitive cleanup/path/build behavior needs both OSes; coverage/live network already stay out of PR lanes. |

## Existing backlog coverage and prerequisite risks

| Existing row | Current audit disposition |
|---|---|
| `edit-integration-fixture-load-amortization-wave-1` | Still relevant; compare minimal fresh fixtures with resettable class fixtures before committing to the older reset design. |
| `workspace-fixture-load-amortization-wave-2` | Confirmed expensive cache/project-mutation suites; preserve explicit cache invalidation and fresh-load cases. |
| `workspace-fixture-load-amortization-wave-3` | Confirmed expensive scaffolding/cross-project suites; preserve project graph mutations. |
| `wire-contract-minimal-workspace-fixture` | Valid bounded setup simplification; no protocol-era coverage reduction. |
| `test-preview-store-cross-class-eviction` | Prerequisite for widening concurrent preview producers; TestServiceContainer uses default PreviewStoreOptions. A larger global cap is not an ownership fix. |
| `test-workspace-fakes-hand-rolled-per-file` | Reuse fail-closed/recording doubles by behavior; do not merge distinct semantics into a permissive universal fake. |
| `test-host-service-provider-composition-dedupe`, `mcp-filter-composition-test-drift` | Deduplicate production-parity composition before proliferating new harnesses. |
| `raw-wire-result-extraction-dedupe`, `changelog-process-tests-runner-adoption`, `pwsh-script-runner-launch-configuration-deduplication` | Existing helper consolidation scope; preserve lifecycle and response/error/notification distinctions. |
| `timing-test-reaudit-wave` | Add watcher-delivery and explicit-mtime cases to the slicing inventory; do not collapse real child-process cancellation coverage into mock-only tests. |
| `test-temp-reaper-live-owner`, `test-temp-path-outside-test-temp-root` | Existing ownership debt remains; keep scoped fixture disposal and prevent cross-run deletion. |
| `test-inconclusive-count-unbudgeted` | Latest TRX reports 20 Windows and 62 Linux total-minus-executed cases. Investigate categories/reasons through the existing row; do not label all as regressions or silently exclude them. |

## Adjacent correctness finding

- `WorkspaceDriftService.cs:90` deduplicates physical file paths with OrdinalIgnoreCase on every OS. Two case-distinct Linux drifted files can collapse into one result.
- Filed `workspace-drift-platform-path-identity`; static evidence only. Require a failing case-sensitive filesystem regression before implementation. This is separate from test-efficiency work.

## Delivery sequence and verification

1. Resolve preview-token ownership for expanded concurrency; implement minimal fixture foundation and measure it on parameter/signature cases.
2. Split the signature class into independently correct behavior groups; preserve case inputs, runtime semantic proofs and discovery/shard integrity.
3. Migrate the measured cache/mutation/scaffold hotspots with the smallest complete fixture change for each behavior.
4. Move the shared validation build and prove private restore/generator concurrency; preserve justified serialization.
5. Retire no-op hooks and confirmed duplicate helpers/cases in bounded batches; repair the private workspace cleanup lifetime.
6. Run repeated focused Windows/Linux checks, then the unchanged required gate for code-bearing work. Compare per-class spans, leg wall times, case identities, skips, failures and resource cleanup.
7. Backlog: sync ai_docs/backlog.md when implementations land. No optimization is claimed implemented or verified by this audit.

## Artifacts

- [Every-file inventory](file-inventory.csv): all 564 tracked paths, structural metrics and disposition; no file is silently omitted.
- [Serialization inventory](serialization-inventory.md): every actual serialization attribute and source anchor.
- [Hosted class durations](hosted-timings.csv): historical per-run/per-OS class sums and case counts.
- [Hosted test-leg walls](hosted-legs.csv): historical test-run wall intervals, not whole-job durations.
- Source/syntax inventory, parser and downloaded TRX remain in the chat scratch artifact directory; historical artifacts are not current-head test execution.

## Audit artifact validation

| Check | Current-session result |
|---|---|
| verify-ai-docs.ps1 | Exit 0; AI docs, upgrade-matrix/notice parity and shipped-skill checks passed |
| backlog-lint.mjs | Exit 0; 325 rows, zero errors; three warnings match a fresh lint of the unchanged primary checkout |
| Coverage integrity | 564 unique existing paths; all inventory paths accounted for; 492 C# files parsed without syntax errors |
| Backlog intake | Eleven new row/detail pairs, four existing rows annotated through the sanctioned writer |
| Scope verification | No src/, tests/, eng/ or workflow diff; primary checkout remains clean at the audited head |
| Shipping | Local audit branch only; not committed, pushed or merged |

The three inherited lint warnings concern the existing executor placeholder anchor and two missing row-header caches (`backlog-d-fragment-schema`, `move-to-git-issues`); they are outside this test-efficiency audit.
