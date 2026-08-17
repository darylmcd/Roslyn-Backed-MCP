# structured-content-projection-test-consolidation — Consolidate projection contract tests

**row:** `structured-content-projection-test-consolidation` · **pri:** `Low` · **size:** `S` · **deps:** `tool-call-error-envelope-wire-contract,tool-error-envelope-sensitive-detail-disclosure`

## Anchors

- `tests/RoslynMcp.Tests/StructuredCallContentProjectorTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs`
- `tests/RoslynMcp.Tests/StructuredContentRoundTripTests.cs`

## Acceptance

- [ ] Keep the complete table-driven decorator invariant in `StructuredCallContentProjectorTests`.
- [ ] Reduce filter coverage to one thin-delegate sentinel and round-trip coverage to producer-owned channel integration.
- [ ] Remove mirrored object/non-object/schema-era assertions so one contract edit cannot leave contradictory green tests.
- [ ] Dispose every `JsonDocument` used by retained projector assertions.
- [ ] All three focused suites remain green after consolidation.

## Evidence

- The three suites explicitly mirror projector behavior; their synthesis expectations drifted in opposite directions during the SDK 2.1 correction.
