# complexity-metrics-platform-path-filter — Use platform path identity for metrics filters

**row:** `complexity-metrics-platform-path-filter` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs`
- `src/RoslynMcp.Roslyn/Helpers/FileSystemPath.cs`
- `tests/RoslynMcp.Tests/CodeMetricsServiceTests.cs`

## Acceptance

- [ ] Canonicalize and compare target paths with the shared platform comparer.
- [ ] Case-sensitive systems do not alias distinct paths that differ only by case; Windows casing variants still match.
- [ ] Single-path and list filters share one implementation and regression shape.

## Evidence

- Metrics filtering hard-codes `OrdinalIgnoreCase` despite the repository's platform-aware path helper.
