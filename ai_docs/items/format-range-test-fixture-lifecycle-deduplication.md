# format-range-test-fixture-lifecycle-deduplication — Share custom format-range fixture lifecycle

**row:** `format-range-test-fixture-lifecycle-deduplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FormatRangeServiceTests.cs:115`

## Acceptance

- [ ] Custom format-range fixture tests use one helper for sample copy, fixture write, workspace load, and guaranteed close.
- [ ] Per-test fixture content and assertions remain explicit; full format-range test class passes.

## Evidence

- Several older tests duplicate the sample-copy/load/try-finally-close sequence. The newer tests already use a helper, leaving two lifecycle patterns in one class.
