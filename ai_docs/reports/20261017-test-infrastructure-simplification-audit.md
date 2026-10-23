# Full test-tree simplification audit — 2026-10-17

## Scope and evidence

| Field | Result |
|---|---|
| Operator scope | Audit all tests for further simplification; preserve assertions, authority checks and existing deadlines. Restore/extraction remain the only shipping prerequisites; paused 15-item batch unchanged. |
| Reviewed tree | Restore worktree, codex/restore-reviewed-graph, HEAD e5edef1272822a4b1edca417d42dbaa67cf5c59b plus preserved dirty work. |
| Full-tree coverage | Every one of 616 C# files under tests was scanned in full: 160,630 lines at inventory time. Non-source test project files, assembly scheduling, fixture lifecycle and eng test orchestration were also inspected. |
| Compiled inventory | Existing metadata-only shard catalog: 568 classes; 5,990 static case weight. AdapterParityVerified=false; this is neither runtime discovery parity nor a suite-pass claim. |
| Method | Full-file pattern/dependency inventory and normalized duplicate-window search; direct source review of common ownership, fixture, runner, timing and cleanup mechanisms plus concrete candidate callsites. |
| Limits | Not a line-by-line manual correctness review of all 160k lines. Pattern matches include comments and embedded test source; counts below are candidate counts unless explicitly confirmed. No full-suite execution or unmeasured runtime savings claimed. |
| Evidence directory | %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/all-tests-simplification |
| Reproducible artifacts | inventory.json; source-manifest.json (per-file SHA256); coverage.md (all 616 files); duplicate-windows.json; compiled-catalog.json. |

## Recommended changes

| Order | Finding and evidence | Smallest complete simplification | Tracking |
|---:|---|---|---|
| 1 | TestBase.cs:180 retains an explicitly empty compatibility DisposeServices method; 176 other files call it. | Delete dead calls/hooks and helper; preserve any real class cleanup and AssemblyLifecycle ownership. No new lifecycle abstraction. | test-class-cleanup-noop-retirement |
| 2 | PerformanceBehaviorTests.cs:247 and WorkspaceExecutionGateTests.cs:975 update maximum concurrency non-atomically; the latter's volatile comment is false. | One atomic/locked concurrency observation helper, with deterministic lost-update proof. Keep real competing operations and budgets. | test-concurrency-observation-atomicity |
| 3 | HardeningBehaviorTests.cs:134 and PerformanceBehaviorTests.cs:220 contain the same runner overload adapter; identical 12-line block appears at 21 locations. | Reuse the existing early-kill semantics through one required-behavior canned runner adapter. Keep real streaming/process runners separate. | command-runner-test-adapter-consolidation |
| 4 | TestRunFailureEnvelopeTests.cs:995 and projection/wire suites repeat prepare/run/complete synthetic adapters; 16 context-check callsites across five files. | Share only canned phase plumbing; retain SyntheticTestRunPlans.RequireSyntheticContext and every cancellation/phase/shape assertion. | test-run-phase-adapter-consolidation |
| 5 | HardeningBehaviorTests.cs:158 and PerformanceBehaviorTests.cs:208 duplicate IWorkspaceManager plumbing; the latter has a silent RestoreVersion no-op. | Use existing FailClosedWorkspaceManagerStub with explicit scenario handlers where contracts match. Keep stateful and lease-intercepting fakes explicit. | test-workspace-fakes-hand-rolled-per-file |
| 6 | 578 copied-sample helper callsites in 75 files. ChangeSignaturePreviewTests.cs:41/67 loads the full graph for a self-contained source-method case. | Small fresh native fixtures selected by actual topology: one project, multiple projects, or packages as required. Preserve real apply, compilation/reference semantics and byte/undo checks. | refactoring-test-minimal-native-fixtures; wire-contract-minimal-workspace-fixture |
| 7 | WorkspaceForkGraphReplayTests is 1,968 lines and mixes inverse ownership, recovery, reference metadata, mapping and real-process execution. Class-level sharding treats it as one unit. | Split by cohesive contract with an explicit old/new case identity map. Share setup only, retain all rows and approved scheduling. | native-replay-test-concern-split |
| 8 | WorkspaceExecutionGateTests.cs:107/139 use 50ms sleeps; EditorConfigServiceTests.cs:225/244 uses 300ms waits; PreviewStoreTests.cs:340 sleeps for expiry. | Event/barrier acknowledgments for scheduling; clock injection for time semantics; bounded outer waits remain. Do not replace real process deadline tests with fake clocks. | timing-test-reaudit-wave |
| 9 | Bare finally deletion can mask primary failures in BuildInputProvenanceTests.cs:95 and BuildPathRolesLiteralItemTests.cs:56. Scan has 368 candidates across 144 files, requiring classification. | Reuse existing CleanupFailureCollector/OwnedResourceCleanup, preserving both failures. Do not create another disposal framework or swallow cleanup errors. | native-test-cleanup-primary-preservation |
| 10 | NuGetInstallationFixture.cs:39-40 launches restore with CancellationToken.None. Formatter/PowerShell suites retain overlapping launch/drain ownership code. | One bounded owned subprocess lifecycle; migrate duplicate callers to existing runner where semantics match. Keep literal argv, EOF, cancellation, tree termination and drain evidence. | fixture-restore-process-lifetime; pwsh-script-runner-launch-configuration-deduplication; changelog-process-tests-runner-adoption |
| 11 | TestServiceContainer.cs:105 uses one default 20-entry preview store across parallel classes; eviction is global in BoundedStore.cs:109. Reachability of interference remains unproved. | Isolate mutable scenario state while sharing immutable fixtures; prove a competing-token reproducer before choosing scoped ownership. Avoid simply increasing capacity. | test-preview-store-cross-class-eviction |
| 12 | summarize-test-results.ps1:102 and collect-hosted-shard-timings.ps1:185 duplicate hardened TRX reading. | One parser, separate report projections; retain DTD prohibition, malformed-record rejection and exact counts. | eng-trx-parser-duplicated-across-two-scripts |
| 13 | TestTempRoot.cs documents age over 24h as proof a run is abandoned, despite long-lived test sessions being possible. | Use live ownership evidence for reclamation; retain per-process roots and never reap an active sibling solely by age. | test-temp-reaper-live-owner |

## Design choices to retain

- Keep distinct authority/refusal, external-byte preservation, real native replay, platform identity and actual-process tests; similarity in setup is not duplicate coverage.
- Keep one assembly disposal owner, per-process temp roots, production DI identity checks, fail-closed stubs, and explicit synthetic-context refusal.
- Prefer fresh small fixtures over resettable shared mutation graphs. Existing load-amortization rows require re-derivation before implementation; state reset adds complexity and order dependence.
- Keep current 30-second undo and 120-second production request deadlines. The operator approved isolation for exactly the three named deadline cases; no blanket isolation or worker-count change follows from this audit.
- Do not introduce one configurable mega-fixture, a new runner framework, global test retries, blanket skips, larger capacities, or looser assertions as simplification.

## Active prerequisite verification

| Change | Evidence |
|---|---|
| Producer marker moved under obj, with exact native membership invariant | Original two cases fail; corrected two pass (10.132s/7.282s). |
| Metadata corruption injected at native publication; approved exact earlier-refusal oracle | metadata-approved/targeted.trx: one pass in 3m53.342s, including all recovery, bytes, version, preview, undo and subsequent-apply checks. |
| Approved deadline scheduling | scheduling-approved/targeted.trx: seven passes; verified-intervals.json proves three deadline cases have zero overlap with unrelated cases. Request limits unchanged. |
| Recovery fixture/helper simplification | Shared ForRecovery forwarding; fresh three-project interop graph. Companion metadata roundtrip passes in 3m21s. |
| Three obsolete prepared-undo rejection hooks | Baseline: three failures because operations succeeded. Shared native rejection callback plus reachability assertions installed; corrected targeted run: all three pass (6m02.913s, 2m54.449s, 3m07.654s), retaining every original assertion and adding injection reachability. |
| Native serialization cleanup | Uses existing failure-preserving helper; all 13 targeted cases pass in the same 16-case run; zero failed or skipped. |

## Delivery boundary

- Audit findings are recorded through the canonical backlog writer; no paused batch state transition is authorized or performed.
- Broader proposed simplifications are recommendations with explicit acceptance, not claims of implemented fixes.
- Restore full-gate qualification, commit/push/PR/merge, extraction integration and final clean-main closeout remain outstanding.

## Implemented simplifications and focused verification

Evidence root: %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/simplification-implementation.
Counts describe focused runs, not full-suite qualification.

| Mechanism | Implementation | Evidence |
|---|---|---|
| Dead cleanup hooks | Removed the no-op DisposeServices helper and callers; retained actual workspace/file cleanup and the assembly disposal owner | No remaining DisposeServices references; actual assembly-fixture Alpha/Beta tests pass in reaper/corrected |
| Concurrency observations | One locked ConcurrencyTracker replaces racy maximum updates; command-runner accounting exits in finally | concurrency/corrected: 39 pass; concurrent peak retention covered; deterministic reproduction of the old internal write interleaving remains outstanding |
| Canned command runners | 22 adapters across 15 files share the required-behavior overload adapter; real streaming runners remain distinct | phases/targeted: 78 pass across phase/wire/coverage contracts; workspace-fakes/targeted: 7 pass including performance cases |
| Synthetic test phases | Eight adapters across five files share preparation/completion plumbing; exact synthetic-context checks remain | phases/targeted: 78 pass |
| Workspace stubs | HardeningBehaviorTests and PerformanceBehaviorTests use FailClosedWorkspaceManagerStub with explicit scenario handlers | workspace-fakes/targeted: 7 pass; unrelated stateful fakes remain explicit |
| Native fixtures | 41 Change Signature and 41 Parameter Object single-project scenarios use fresh native projects; dedicated cross-project setups remain | minimal-native: same two cases fall from 4m27s combined to approximately 55s; native-workloads: 17 pass; parameter-fixtures/targeted: 8 pass in 2m54s |
| Fixture cleanup | Changed native fixture callers preserve primary failures through existing cleanup helpers; eight confirmed native build-input cleanup blocks repaired | cleanup/targeted: 69 pass; cleanup-change-signature/targeted: 12 pass including real apply and cleanup failure controls |
| Restore setup lifetime | Migrated shared fixture restore callers have a 120-second cancellation budget; direct setup/repair calls remain tracked by fixture-restore-process-lifetime | native-workloads: 17 pass; existing request deadlines unchanged |
| Replay organization | Original 38 test methods split into graph, inverse ownership and recovery classes with shared setup; method attributes/data rows preserved | splitter/case-map.json records original/new names and pre-format method hashes; replay-workload: seven replay/deadline cases pass |
| Identity oracle | Explicitly approved Directory identity, file-content refusal and exclusive-file capture refusal replace inconsistent directory-capture expectation | identity-read-oracle: original case fails; corrected Windows 20 pass/2 platform skips; Linux identical compiled output 13 pass/9 platform skips |
| TRX input | Both reporting scripts share hardened XML/duration readers; report-specific projections remain separate | trx/corrected: 17 pass |
| Temp reaping | Requires both stale age and confirmed process exit; live/unknown owners retained | reaper/baseline: live-owner test fails; reaper/corrected: 15 pass |

- Follow-up design remains for deterministic scheduling observations, formatter process ownership, changelog stdin ownership and broader workspace fake migration.
- Cross-class preview-store interference remains a hypothesis requiring a competing-token reproducer; no capacity increase or blanket serialization was introduced.
- The 368 cleanup scan matches require classification; only confirmed defects above are claimed fixed.
- Local aggregate, hosted exact-head checks and shipping remain required. The paused batch is unchanged.
