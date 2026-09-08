# workspace-warm-repeat-cache-causal-regression — Make repeat-warm coverage causal

**row:** `workspace-warm-repeat-cache-causal-regression` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceWarmServiceTests.cs`

## Acceptance

- [ ] Remove the wall-clock ratio assertion from the repeat-warm test.
- [ ] Assert the causal cache transition instead: the isolated first warm reports at least one cold compilation and the unchanged second warm reports zero.
- [ ] Keep one regression shape that is insensitive to runner speed, timer resolution, and scheduling noise.

## Evidence

- `WorkspaceWarmServiceTests.WarmAsync_SecondCall_NoCold_FasterThanFirst` requires the second call to be ten times faster even though the neighboring test already documents wall-clock timing as unsuitable for correctness.

