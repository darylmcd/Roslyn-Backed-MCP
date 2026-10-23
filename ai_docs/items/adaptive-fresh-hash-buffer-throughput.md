# adaptive-fresh-hash-buffer-throughput — Grow fresh hash buffers after sustained reads

**row:** `adaptive-fresh-hash-buffer-throughput` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/StreamContentHash.cs`
- `src/RoslynMcp.Roslyn/Services/PhysicalInputCaptureBatch.cs`: resource-bound comment.
- `tests/RoslynMcp.Tests/StreamContentHashTests.cs`
- `tests/RoslynMcp.Tests/StreamContentHashAdaptiveTests.cs`

## Acceptance

- [ ] Start64KiB; grow once to256KiB after256KiB observed data without querying length or assuming complete reads.
- [ ] Preserve full fresh SHA256 throughEOF, partial reads, appended data, exact failure/token propagation and caller stream ownership.
- [ ] Clear the entire owned array on success/failure; return old buffer before renting larger; retain at most one active buffer per capture.
- [ ] Pass unchanged existing tests and new nonseekable boundary, appended-data and expanded-lifetime cancellation/failure cases; observe the new branch controls fail with old64KiB behavior.
- [ ] Preserve all authority checks and production budgets; verify combined native acceptance independently of microbenchmark improvements.

## Evidence

- Parent-run hash-adaptive-diagnostic-a2c0ca4a8bd041be910f0f8140f7164a passed exact hashes across24 interleaved samples per variant/size.
- Adaptive hash medians improve approximately13% at1MiB and20% at8/32MiB, with approximately6-9% threshold-size cost. No native deadline resolution is established.
- Current proposal is scratch-only and new controls are unrun.

Installed adaptive buffer after current-session interleaved benchmark and independent cold review. adaptive-before43:5pass2expectedfail7; expanded-read cancellation/failure hooks were unreachable with old64KiB buffer. New helper uses observed bytes only, releases/zeros old array before growth, preserves caller stream ownership and full EOF hashing. Batch active requested buffer bound updated to1MiB. Focused after44 and combined acceptance pending; no request-deadline claim.

after44 passed39/39 in27s. Combined focused45 passed261 and failed3 of264 in3.7468min: both original30s ConcurrentUndoRoutes rows and original120s ActualTestProcess request timeout remain red. All newadaptive and normalization controls passed. No timeout/assertion change installed; no fullgate/ship acceptance claimed.
