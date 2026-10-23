# test-run-phase-adapter-consolidation — test-run-phase-adapter-consolidation

**row:** `test-run-phase-adapter-consolidation` · **pri:** `Low` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs:995`
- `tests/RoslynMcp.Tests/TestRunPublicProjectionTests.cs:619`
- `tests/RoslynMcp.Tests/Helpers/SyntheticTestRunPlans.cs:17`

## Acceptance

- [ ] Extract only common canned-result phase plumbing with required result behavior.
- [ ] Preserve RequireSyntheticContext checks in execution and completion, cancellation checks, plan fields, and all wire/projection assertions.
- [ ] Keep real native runner and phase-authority tests independent; enumerate and migrate all copies of the same canned adapter.

## Evidence

Full-tree duplicate scan found repeated phase bodies across failure-envelope, projection, timeout-wire and compact-validation tests. Sixteen RequireSyntheticContext callsites occur in five files.

- Source: operator-requested full test-tree simplification audit, 2026-10-17.
- Preserve the paused batch; this row records findings, not batch advancement.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.

2026-10-23 full current-source review confirms CallbackExecutionExecutor, ThrowingExecutionExecutor and CannedExecutionExecutor (~1004/1138/1188) repeat newly required ExecuteAuthorized restore grant/CompleteFixtureRestore and ExecuteTest authorize/test-no-restore/execute/collect/redact protocol. Extract small shared required fixture functions preserving canned report-write/callback/throw differences and every actual contract assertion. No optional compatibility shim/new framework; source-only finding, qualification pending.
