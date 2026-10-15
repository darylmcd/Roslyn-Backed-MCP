# fixture-git-add-hosted-deadline — Diagnose fixture Git staging deadlines

**row:** `fixture-git-add-hosted-deadline` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs:103-138`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs:102-170`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs:25-35`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs:85-95`

## Acceptance

- [ ] Reproduce against an immutable base with the same Windows image and shard; distinguish native Git runtime, redirected-pipe completion, managed scheduling and fixture contents through process lifetime evidence.
- [ ] Fix the evidenced root cause across every consumer of the affected process mechanism; retain the configured deadline, surfaced failures, bounded process-tree cleanup and literal argument handling.
- [ ] Observe causal old-behavior failure and repaired progress through the actual shared helper; include nonzero exits, cancellation, inherited pipes and timeout cleanup controls.
- [ ] Require fresh local and exact-head hosted gates. Do not classify the failure as a flake or retry an unchanged red gate to obtain green.

## Evidence

| Evidence | Observation |
|---|---|
| PR1781 head | `f80a0d893dbe901c2937eb496e6d99e5786d928e`; fixture helpers and affected test class are byte-identical to base `e68c0e4819ef750c239b6b6d2bb1426af3be08ff`. This is not a base reproduction. |
| Hosted failure | [Run38764978529](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/38764978529), Windows shard4: `CompilationCompleteness_PreservesFailuresAndScope` and `TestPhaseTimeout_AndCallerCancellationStillPropagates` timed out during `git add` fixture setup after30seconds; empty captured output. |
| Local gate | Same head passed full unsharded `just ci`:4143passed,0failed,19skipped; build, publish/hash and audit passed. Hosted failure remains unresolved. |
| Base probe | [Run38768845490](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/38768845490) checks out exact base `e68c0e48`, reproduces Windows shard4, and records native Git start/exit lifetimes. Pending when filed. |

## Context

- One regression shape: fixture staging exceeds its process deadline without enough evidence to distinguish native-process delay from completion/drain delay.
- `GitFixtureRunner.RunGitCapture` synchronously blocks on the shared asynchronous runner. Investigate that scheduling risk; do not claim it caused this hosted failure without reproduction.

Related umbrella gate-timing-tests-fail-under-machine-load includes earlier validation scope/count and pwsh resolver/cancellation timing observations. This focused row tracks the distinct native-Git staging deadline witness and needs its own process-versus-drain reproduction; consolidate only if evidence establishes the same root cause.

2026-10-15: Base Windows shard probe run38768845490 passed (52 native git add completions; maximum4.498seconds), so the original hosted failure was not naturally reproduced. An isolated one-worker executable invoking the actual base helper failed after native Git exit with three queued continuations; the async helper completed. PR1785 migrates every helper caller and the backdated commit runner without increasing the30second deadline. This proves the scheduling defect, not causality for the hosted timeout; fresh hosted validation is pending.
