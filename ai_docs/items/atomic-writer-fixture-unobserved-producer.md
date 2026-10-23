# atomic-writer-fixture-unobserved-producer — Observe atomic writer failure while awaiting fixture readiness

**row:** `atomic-writer-fixture-unobserved-producer` · **pri:** `Medium` · **size:** `S` · **deps:** `obsolete-atomic-file-writer`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`: retained actual composite orchestration; obsolete helper removed.
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`: retained actual composite/undo controls; helper-only tests removed.

## Acceptance

- [ ] Superseding disposition: retire the nonexistent helper-only producer fixture with obsolete-atomic-file-writer; do not recreate the unused writer or overlap test.
- [ ] Reconfirm zero production callers before removal and preserve actual composite mutation, undo, encoding and temporary-artifact controls.
- [ ] Verify the focused current composite suite and merge the independently correct retirement before closing this row.
- [ ] Keep the historical unobserved-producer evidence below as the reason the former fixture was not trustworthy.

## Evidence

| Source | Observed mechanism |
|---|---|
| :75-80 | First writer may fail during temporary acquisition or WriteAsync before setting firstPrepared |
| :82 | Unbounded firstPrepared.Task await is outside try/finally and never observes the producer's failure |
| :85-96 | If the second writer throws, finally releases the first writer but control skips the later await first, leaving owned work unobserved |
| :333-340 | Same file already races a callback against its producer, awaiting the producer to surface native failure |

- Read-only finding; no reproduction or test execution claimed.
- Existing atomic-file-writer-external-temp-path-swap tracks production pathname replacement, not this fixture coordination failure. Other matching composite rows cover path identity, undo authority and encoding, not producer observation.
- Unrelated pre-existing test mechanism; track separately from restore/extraction prerequisite implementation.


2026-10-23 superseding disposition: semantic inventory found zero production callers of AtomicFileWriter; the dead class and sixteen helper-only tests were removed in the active unmerged change. The old fixture repair acceptance is superseded by retirement, not implemented by recreating unused production code. Current actual composite/undo regression controls remain. This row stays open until the retirement lands and current validation is complete.
