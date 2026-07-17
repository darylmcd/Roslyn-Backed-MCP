# compositeapply-temp-cleanup-failure-observability — Surface composite-apply cleanup failures

**row:** `compositeapply-temp-cleanup-failure-observability` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:130-160`

## Acceptance

- [ ] Temporary-file cleanup failures are no longer silently swallowed.
- [ ] Cleanup failure is logged with the path and exception without masking the primary apply outcome.
- [ ] A regression forces cleanup failure and asserts one observable warning/error plus preservation of the primary result.

## Evidence

- Cold apply/undo review found a catch path that discards temporary cleanup failure.
## Validation

- Add the cleanup-failure regression to `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`; do not create a second orchestration fixture.
