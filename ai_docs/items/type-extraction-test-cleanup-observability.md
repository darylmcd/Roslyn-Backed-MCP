# type-extraction-test-cleanup-observability — Stop swallowing fixture cleanup failures

**row:** `type-extraction-test-cleanup-observability` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Replace `TryDeleteDirectory`'s catch-all suppression with bounded cleanup that preserves the primary test failure and reports cleanup failure.
- [ ] Distinguish expected transient Windows handle contention from unexpected filesystem failures.
- [ ] Add one regression for cleanup failure reporting without leaking a fixture directory.

## Evidence

- `TryDeleteDirectory` catches every exception and emits no diagnostic, hiding fixture leaks and unexpected filesystem failures.
