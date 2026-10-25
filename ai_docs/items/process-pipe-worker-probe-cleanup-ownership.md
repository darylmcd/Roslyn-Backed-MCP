# process-pipe-worker-probe-cleanup-ownership — Preserve isolated probe failure and cleanup ownership

**row:** `process-pipe-worker-probe-cleanup-ownership` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/ProcessPipeWorkerProbe/Program.cs` — existing held-pipes/large-output branch finally; release-file write precedes pool reset and unbounded completion waits.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs` — existing isolated worker/progress probe callers and ownership assertions.

## Acceptance

- [ ] Observe RED for a real old-scenario failure where release or cleanup faults; preserve the primary failure and every independent cleanup failure without skipping remaining owners.
- [ ] Independently attempt child release, isolated pool restoration, owned process termination/reap and reader observation, preserving the existing behavioral and cleanup budgets. Never delete dependent material before its owners finish.
- [ ] Bound existing completion waits and report pending ownership honestly. Cover all instances of this old scenario cleanup mechanism and preserve exact output, cancellation, inherited-pipe and worker-progress controls.
- [ ] Keep pool settings confined to the disposable probe process; do not alter the parent testhost, weaken assertions or increase deadlines. Complete fresh review and affected/hosted qualification before closure.

## Evidence

- Current-session source inspection found sequential release-file write, fixed pool reset and unbounded GetAwaiter().GetResult waits in the preexisting probe branch; an early cleanup error can skip later cleanup or replace the primary failure.
- This is a source-level failure-path finding; no causal cleanup-fault regression has been executed for the old branch.
- The new formatter-capture branch in PR1828 has separate cleanup ownership. Its worker-retention regression and reader repair do not fix this existing branch.
- Cold review classified this as a separate cleanup mechanism, independent of native pipe progress; no inline expansion of PR1828 is required.

## Context

- Independent nonblocking follow-up; no dependency from restore-callers-missing-packages-path or the finite closeout.
