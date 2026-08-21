# analyzer-shadow-reclamation-diagnostics-preservation — Preserve reclamation failure evidence

**row:** `analyzer-shadow-reclamation-diagnostics-preservation` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs`

## Acceptance

- [ ] The reload reclamation assertion reports the last deletion error and the old load context's liveness state.
- [ ] Remove the boolean-only clean-stack wrapper after its final caller is migrated.
- [ ] One regression proves a forced reclamation timeout retains the diagnostic evidence without waiting 30 seconds.

## Evidence

- The adjacent coverage-failure audit found that the reload branch discards `lastError` and `firstContextRef.IsAlive`, while the later close branch preserves both; the boolean-only wrapper exists solely for that lossy call.
