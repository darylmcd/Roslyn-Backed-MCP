# timing-test-reaudit-wave — Re-audit the remaining wall-clock-sensitive tests

**row:** `timing-test-reaudit-wave` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:68`
- `tests/RoslynMcp.Tests/HostShutdownLifecycleTests.cs:28`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:153`
- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:113`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs:181`

## Acceptance

- [ ] Each fixed-deadline test below is either made deterministic (injected `TimeProvider`/delay, event-based wait, structural assertion) or its deadline is justified as a bounded-vs-unbounded check with a measured failure duration: `DotnetCommandRunnerPipeLifetimeTests.cs:68` (10 s CTS around a pwsh cold start), `DiagnosticQueryServiceRegressionTests.cs:507,565`, `ExternalEditStalenessTests.cs:153`, `HostShutdownLifecycleTests.cs:28-44`, `McpLoggingLifecycleWireTests.cs:229`, `WorkspaceForkApplyCancellationTests.cs:138`, `ScriptingServiceTests.cs:587`, `PerformanceBehaviorTests.cs:113,136`, `Helpers/PwshScriptRunnerTests.cs:181-198,244`.
- [ ] The warm-cache speed-up signal dropped from `WorkspaceWarmServiceTests` (the 10x ElapsedMs assertion) gets an honest home, or is recorded as intentionally structural-only (`PerformanceBehaviorTests` has no warm coverage today).
- [ ] Re-run the audit probe `rg 'WaitAsync\(TimeSpan|Elapsed <|CancellationTokenSource\(TimeSpan' tests` (about 60 hits in about 35 files) and list any new candidate.

## Evidence

- Deepener of `load-sensitive-timing-tests-full-suite` (plan 20261001T034545Z) listed these as load-sensitive candidates left out of its PR (one regression shape per PR).

## Context

- Split child of `load-sensitive-timing-tests-full-suite`; sequence with `load-profile-full-suite` evidence if it lands first.

2026-10-17 full-corpus syntax scan: include EditorConfigServiceTests.cs:225 watcher-delivery sleep, Services/WorkspaceDriftServiceTests.cs:103,174 timestamp sleeps, PreviewStoreTests.cs:288 TTL sleep. Use observable event delivery or explicit mtime/clock control, not shorter waits. PwshScriptRunnerTests inherited-pipe cases measured about 25s/40s; retain actual native-pipe/teardown proof. See ai_docs/reports/20261017-test-efficiency/report.md.

2026-10-17 mutation gate follow-up: one verifier output-drain timeout was not reproduced on clean base or in the instrumented suite. Controlled isolated probes established Windows synchronous-pipe worker starvation before and after native exit; both regressions failed on the old runner and passed with nonblocking available-byte reads. The same validation exposed and repaired fragmented BOM decoding. Preserve the original attribution limit, unchanged deadlines and inherited-pipe tests; evidence: ai_docs/reports/20261017-test-efficiency/output-drain.md.

2026-10-25 PR1828 attribution follow-up:
- fb3b2601 Windows2 ReleaseLagGuard total30s deadline expired after managed parent exit with stdout pending; cleanup collected verifier PASS. Native exit/pipe timing was absent, so the natural cause remains unproven.
- Exact run40155771898 plan reconstruction shows the new class moved ReleaseLagGuard and FormatterBaselineContractTests onto the same shard; formatter overlapped the entire failure. The undo class itself ran on Windows4. This is coupling evidence, not causal attribution.
- Actual formatter-capture isolated OLD probe:1FAIL/0PASS, live child and two pending captures, sentinel blocked and zero available workers in327/327samples over5006ms; exact output/snapshots drained after release. This proves the raw synchronous-pipe reader resource hazard; PR1828 repair/qualification is pending.
- Verified deadline diagnostics are in1aa53356; no unchanged CI retry, deadline increase, worker cap or ratchet baseline change. Preserve the original incident attribution limit after any resource-hazard repair.
- Evidence: <user>/.codex/scratch/roslyn-remediate-20261023-split/release-lag-ci-independent-cause-review.md and formatter-capture-red-session.log.
