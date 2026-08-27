# meta-projection-failure-observability — Observe unexpected text-metadata projection failures

**row:** `meta-projection-failure-observability` · **pri:** `Low` · **size:** `S` · **deps:** `tool-error-envelope-sensitive-detail-disclosure`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` — `InjectMetaIfPossible`.
- New `tests/RoslynMcp.Tests/MetaProjectionObservabilityTests.cs`.

## Acceptance

- [ ] Keep non-JSON and non-object text as explicit pass-through outcomes without logging noise.
- [ ] Distinguish an unexpected metadata serialization/projection failure from normal pass-through and emit one secret-safe correlated diagnostic.
- [ ] Never change the original MCP result or include the response payload, path, or exception message in diagnostics.
- [ ] One injected-failure regression proves the original text survives and the safe diagnostic is observable.

## Evidence

- `ToolErrorHandler.InjectMetaIfPossible` ends in a bare `catch` that silently converts every unexpected projection defect into an unchanged response.
