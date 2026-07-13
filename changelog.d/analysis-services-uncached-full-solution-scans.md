---
category: Maintenance
---

- **Maintenance:** Reduced redundant full-solution scans in `CouplingAnalysisService.ComputeAfferentCouplingAsync` (cross-project semantic-model lookups now route through the shared `ICompilationCache` instead of raw Roslyn Document APIs) and `ImpactSweepService`'s persistence-layer sweep (the mapper-candidate enumeration is hoisted out of the per-DTO-sibling loop) — no behavioral change, faster `get_coupling_metrics` and `symbol_impact_sweep` responses on multi-project solutions.
