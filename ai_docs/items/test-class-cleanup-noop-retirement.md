# test-class-cleanup-noop-retirement — test-class-cleanup-noop-retirement

**row:** `test-class-cleanup-noop-retirement` · **pri:** `Low` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs:180`
- `tests/RoslynMcp.Tests/PreparedUndoCallerTests.cs:16`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs:36`

## Acceptance

- [ ] Remove all 176 DisposeServices callsites and the empty helper; preserve any real class cleanup statements around them.
- [ ] Keep AssemblyLifecycle disposal and its observable release assertions; do not change service lifetime or test parallelism.
- [ ] Prove the compiled test case catalog and all assertion bodies unchanged; build and run lifecycle controls.

## Evidence

Full-tree scan found 177 occurrences in 177 files: one method definition and 176 calls. The method explicitly does nothing for source compatibility.

- Source: operator-requested full test-tree simplification audit, 2026-10-17.
- Preserve the paused batch; this row records findings, not batch advancement.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.
