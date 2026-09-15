# symbol-disambiguation-response-byte-budget

**row:** `symbol-disambiguation-response-byte-budget` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`

## Acceptance

- [ ] Reproduce a high-fan-out ambiguous metadata name with long candidate locations. Bound serialized candidate responses and provide a complete recoverable candidate-selection route without altering successful single-symbol dispatch.

## Evidence

- 2026-09-15 direct source review: FindReferences and GoToDefinition return TryDisambiguateMetadataNameAsync.ListEnvelope before reference-page byte accounting; ambiguous symbol candidate payloads remain unbounded.
