# change-tracker-subscriber-failure-isolation

**row:** `change-tracker-subscriber-failure-isolation` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeTracker.cs`
- `tests/RoslynMcp.Tests/ChangeTrackerTests.cs`

## Acceptance

- [ ] Invoke each ChangeRecorded subscriber once in registration order; catch and report each subscriber failure independently while continuing to later subscribers. A regression with a throwing first subscriber and recording second subscriber proves notification continuation and that the recorded change remains available.

## Evidence

2026-09-15 source review: RecordChange invokes the complete multicast delegate under one try/catch. A throw from the first subscriber skips every later subscriber, including consumers that synchronize undo state.
