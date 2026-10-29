# Dropped ledger — refactor-audit 20261028-1500

| Candidate | Exit | Reason |
|---|---|---|
| ci-map `duplicate-command` `.github/workflows/ci.yml:128`/`:395` and `:142`/`:399` | refuted | `validate` runs only when `evidence_only != 'true'`, `evidence_lint` only when `== 'true'` (ci.yml:98, :384): the jobs are mutually exclusive. ci-map does not model job `if:` exclusivity (tool false positive; reported, not filed) |
| ci-durations `shard-imbalance` windows-hosted-1-of-4, `duration-drift` windows-hosted-3-of-4 / 4-of-4, `duration-drift` publish | out-of-focus | Performance dimension; neighbour row `ci-quality-move-hosted-timing-recheck` |
| gitleaks `generic-api-key` `.tmp/cycle3-analyzer-offline-proof/**` (4) | out-of-focus | gitignored cache (RULES 14); analyzer XML text |
| gitleaks `generic-api-key` `ai_docs/plans/20261007T211247Z_backlog-remediate/state.json:1060,1061,3916,3917`; `sourcegraph-access-token` `audit-reports/application-brainstorm.md:6` | out-of-focus | outside `.github/** tests/** eng/**`; masked read shows long identifiers/SHA-shaped values, not credentials |
| gitleaks `generic-api-key` `tests/RoslynMcp.Tests/PublicExceptionDetailPolicyTests.cs:56`, `ToolCallErrorWireContractTests.cs:44` | refuted | test sentinels (`secret-sentinel-47`, `SECRET-SENTINEL-9f13c2`) asserted absent from output; not credentials |
| `Assert.Inconclusive` platform and fixture-drift skips (36 files; `AnalysisToolsTests.cs:310`, `AnalyzerInfoToolsTests.cs:98`, `Services/WorkspaceDriftServiceTests.cs:208`) | merged-into test-inconclusive-count-unbudgeted | live row names the same mechanism and sites |
| `Thread.Sleep` / `Task.Delay` / `WaitAsync` timing sites across tests | merged-into timing-test-reaudit-wave | live row owns the probe and the re-audit |
| `PerformanceBaselineTests.cs:56-170` elapsed budgets (8-30 s) | refuted | class is `[TestCategory("Performance")]`, serialized, header documents the budget as the product signal |
| `ScriptingServiceTests.cs:50,580` elapsed bounds | merged-into timing-test-reaudit-wave | listed in that row |
| `eng/verify-release-environment.ps1` has no test | merged-into release-environment-precheck-contract-tests | live row |
| `profile-large-solution.ps1` hardcoded default path | merged-into profile-large-solution-hardcoded-default-path | live row |
| `eng/gen-mcp-tool-metadata.ps1` (no reference, no test, input list removed), `eng/list-skills.ps1` (no test by filename) | out-of-focus | DeadCode/Documentation; recommended as rows in the report |
| `publish-nuget.yml` repo-scoped `NUGET_API_KEY`, no `environment:`, default-branch-only ruleset | refuted | `gh api .../collaborators` → `darylmcd` is the only principal with push; an environment gate would add approval friction with no second actor to constrain. Re-open if a second collaborator is added |
| Assertion-free test scan (59 suspects) | below-floor | heuristic fooled by methods declared inside raw-string fixtures (`TestDiscoverFqdnDriftTests.cs:40`) and nested braces; sampled 4, 0 real |
| Dead-premise row ids in test comments (37 unresolved tokens) | below-floor | 3 id-shaped hits resolve via `git log -S`; the rest are prose phrases and test names; no unresolved row id |
| `Program.cs` polling loops in `GitFixtureWorkerProbe` / `ProcessPipeWorkerProbe` | below-floor | iteration/elapsed readiness polls inside probe workers, not test pass/fail bounds |
