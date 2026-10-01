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
