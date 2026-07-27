# coupling-analysis-compute-hotspot-decomposition — Decompose coupling-analysis hotspots

**row:** `coupling-analysis-compute-hotspot-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs` (`ComputeEfferentCouplingAsync`, `GetCouplingMetricsResultAsync`)
- `tests/RoslynMcp.Tests/CouplingAnalysisTests.cs`

## Acceptance

- [ ] Split candidate enumeration, per-type execution, and efferent-symbol classification into focused helpers.
- [ ] Reduce `ComputeEfferentCouplingAsync` below cyclomatic complexity 12 without changing coupling counts or partial-result semantics.
- [ ] Preserve cancellation and deterministic ordering in focused regressions.

## Evidence

- Live Roslyn complexity metrics on 2026-07-26 reported complexity 16 for `ComputeEfferentCouplingAsync` and a 97-line result orchestration method.
