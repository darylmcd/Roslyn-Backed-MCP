# analyzer-shadow-abandon-marker-recycled-pid — AbandonUnownedMarker can leave a markerless child that a recycled creator PID keeps alive

**row:** `analyzer-shadow-abandon-marker-recycled-pid` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`

## Acceptance

- [ ] An exception after some loaders are bound never leaves a child that a recycled PID preserves indefinitely; decision recorded with a test.

## Evidence

- PR 1996 re-review finding 4.
