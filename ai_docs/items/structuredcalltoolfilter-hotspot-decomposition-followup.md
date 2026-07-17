# structuredcalltoolfilter-hotspot-decomposition-followup — Finish StructuredCallToolFilter hotspot decomposition

**row:** `structuredcalltoolfilter-hotspot-decomposition-followup` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:92`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallContentProjector.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs`
- `tests/RoslynMcp.Tests/StructuredContentRoundTripTests.cs`

## Acceptance

- [ ] Make `StructuredCallToolFilter.Create` thin orchestration and move elicitation/retry flows (`TryElicitAndRetryAsync`, `TryElicitChoiceAsync`) into one focused collaborator with direct tests.
- [ ] Move structured-content/meta projection (`InjectMetaIntoContent`) into one focused collaborator while preserving every text/structured-content wire shape.
- [ ] Materially reduce `StructuredCallToolFilter.cs` LOC and keep each moved/original hotspot within the repository's complexity threshold.
- [ ] Preserve cancellation, retry, error classification, schema lookup, and metrics behavior across direct and integration regressions.

## Evidence

- The 20260713 plan review found PR #1049 completed allowlist and metrics extraction but left the original row's third acceptance bullet open: `Create`, `TryElicitAndRetryAsync`, `TryElicitChoiceAsync`, and `InjectMetaIntoContent` remain in the filter.

## Context

Close the original extraction row during reconciliation because its planned slice shipped. This follow-up tracks the remaining root-cause decomposition explicitly rather than silently treating partial acceptance as complete.

## Notes

- Coordinate with the already-extracted `ElicitationAllowlistPolicy` and `CallMetricsRecorder`; do not fold their responsibilities back into the filter.
