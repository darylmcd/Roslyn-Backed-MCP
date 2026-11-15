# namespace-deps-circular-only-edge-leak — Return complete cyclic namespace coverage

**row:** `namespace-deps-circular-only-edge-leak` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceDependencyService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:316`

## Acceptance

- [ ] Use strongly connected components internally to determine cyclic membership. Report C in the red-first graph `A→B`, `B→A`, `A→C`, `C→B` even when DFS finishes B before visiting C.
- [ ] `circularOnly=true` keeps only edges on a cycle. Assert a one-way edge between two cyclic components is excluded and that the analyzed project and declared namespace counts are preserved.
- [ ] Each circularDependencies[].cycle remains a simple directed cycle with the start node repeated at the end. Consecutive entries follow reported edges and no interior namespace repeats. A component can have multiple witnesses. Qualify the stable payload before selection.

## Evidence

- circularOnly=true returns Host.Stdio.Tools→Core.Models (34), which is on no cycle. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`

## Notes

Namespace ownership review at d9e2b3ff. DFS over A→B, B→A, A→C, C→B can finish B before seeing C, then miss C even though all three are strongly connected. Both this and the existing edge leak affect stable circularOnly results. Select after qualifying the cycle payload under 5.x, or at the next approved major if the complete repair requires a breaking change.
