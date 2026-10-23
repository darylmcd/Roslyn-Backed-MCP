# command-runner-test-adapter-consolidation — command-runner-test-adapter-consolidation

**row:** `command-runner-test-adapter-consolidation` · **pri:** `Low` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/HardeningBehaviorTests.cs:134`
- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:220`
- `tests/RoslynMcp.Tests/Helpers/CommandRunnerTestResults.cs:9`

## Acceptance

- [ ] Use one narrowly scoped adapter for the identical overload-forwarding mechanism; require the scenario execution callback rather than permissive default behavior.
- [ ] Preserve argument capture, cancellation, early-kill patterns and outcomes; retain distinct real-streaming and process-lifecycle implementations.
- [ ] Enumerate every identical adapter instance and prove existing assertions against the common implementation; no blanket merging of runners with different contracts.

## Evidence

Normalized 12-line scan identified an identical recording/forwarding block at 21 locations. ApplyEarlyKill is called 22 times in 15 files; the common semantic helper already exists.

- Source: operator-requested full test-tree simplification audit, 2026-10-17.
- Preserve the paused batch; this row records findings, not batch advancement.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.
