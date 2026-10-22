# Refactoring test efficiency implementation

<!-- scope: in-repo -->

| Field | Evidence |
|---|---|
| Audit | [Repository audit](report.md); shipped in PR #1791 |
| Source baseline | c96bf6ad997e3fbb94a8f2ccc9dfe5d19232c4c1; test sources identical to 000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7 |
| Scope | Change Signature and Parameter Object fixtures; no product implementation or CI policy changes |
| Service ownership | Each test invocation owns a production TestServiceContainer; preview limits and TTL remain unchanged |
| Fixture graph | Self-contained cases copy SampleLib sources, obj and root build support files, then load its csproj; explicit cross-project cases retain SampleSolution |
| Scheduling | Nine distinct signature TestClass types; 333 cases preserved; helpers have no TestMethod attributes |
| Cleanup | Close each workspace before provider disposal and directory deletion; aggregate failures; roll back failed fixture copies |
| Remaining ownership debt | test-preview-store-cross-class-eviction remains open for other assembly-shared producers |
| Separate product debt | bounded-store-capacity-atomic-insertion remains open; this change does not alter its eviction algorithm |

## Local Windows experiment

| Run | Before seconds | After seconds | Passed per run | Skipped per run |
|---|---:|---:|---:|---:|
| 1 | 693.737 | 218.223 | 435 | 0 |
| 2 | 838.453 | 202.435 | 435 | 0 |
| Median | 766.095 | 210.329 | — | — |

- Reduction: 72.5% in the focused test interval; 3.64 times faster.
- Interval: first case start through last case end from TRX; excludes restore, build and adapter startup.
- Runs were serialized on the same machine with Release binaries and eng/ci.runsettings.
- Source baseline and optimized executions contain the same 435 test-name/data identities: 333 signature plus 102 parameter-object cases.
- Two samples expose local variation but do not establish a whole-CI speedup; class sums are not additive wall time.
- [Raw timing summary](refactoring-local-timings.json) retains both run and case intervals.
- Fresh Linux baseline: audit PR #1791, CI run 39070458064; signature 806.781 seconds and parameter-object 208.288 seconds of case time. Implementation Linux outcomes and repeated hosted timings belong to the implementation PR's check artifacts.

## Reproduce

| Concern | Command / selection |
|---|---|
| Build and prepare | dotnet test tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj -c Release --settings eng/ci.runsettings with the focused filter below |
| Repeat timing | Same command with --no-build --no-restore -p:TestFixturesPrepared=true; unique TRX logger/result paths |
| Before filter | Exact ClassName predicates for RoslynMcp.Tests.ChangeSignaturePreviewTests and RoslynMcp.Tests.ParameterObjectPreviewTests |
| After filter | Exact ClassName predicates for ChangeSignaturePreviewTests, ChangeSignatureActivatorTests, ChangeSignatureAliasesTests, ChangeSignatureBindingsTests, ChangeSignatureCollectionsTests, ChangeSignatureConstructorsTests, ChangeSignatureRefusalsTests, ChangeSignatureStructConstructionTests, ChangeSignatureStructInitializationTests and ParameterObjectPreviewTests; all under RoslynMcp.Tests |
| Full local gate | just ci |
| Hosted gate | Unchanged code PR matrix: four Windows shards, two Linux shards, exact SDK floor |
| Case mapping | [Method-to-behavior mapping](signature-class-map.json); metadata-shape controls and parameter-object methods remain in their original classes |

## Coverage and negative controls

| Check | Observed result |
|---|---|
| Source inventory | 152 methods including two unchanged metadata-shape controls; every existing method and data attribute preserved |
| Assertion comparison | No unexpected changes before formatting; only service qualification, fixture factory and signature-service access changed |
| Executed identity comparison | 435 before/after case identities, zero differences |
| Ownership regression on shared services | Failed: the first live token was null after the second consumer stored 64 previews |
| Ownership regression on owned services | Passed; unrelated pressure preserves the live token while eviction remains active within the owning store |
| Copy-cleanup negative control | Removing rollback produced two failures for partially copied roots; rebuilt fixed helper passed |
| Lifecycle checks | 18 passed; six existing Windows symlink cases inconclusive; all five added cases executed |
| Reload and close | Owning manager preserves one-reload token validity, invalidates beyond the version span, and closes before deleting the root |
| Two- and four-shard metadata plans | 419 concrete classes; 4,525 static case weight; complete, disjoint partitions; signature weight remains 333 |
| Formatter | Zero new or baseline findings in the changed C# set at local preflight |

The local experiment is separate from final merge qualification. Use the implementation PR's current-head full checks and TRX artifacts for hosted validation and discovered-class adapter parity.
