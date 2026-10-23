# analysis-test-filter-repeated-fullgraph — Reuse one native graph per analysis filter

**row:** `analysis-test-filter-repeated-fullgraph` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`: project test-classification filter.
- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs`: project test-classification filter.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs`: IsTestProject(Project).

## Acceptance

- [ ] Capture one fresh request-local evaluated graph and resolve each selected owner's exact context for both analysis filters.
- [ ] Preserve ad-hoc and missing-file fallback behavior and conditional multi-target test classification.
- [ ] Verify imported test properties across multiple projects and canceled requests without caching authority between requests.

## Evidence

- Restore prerequisite read-only review found per-project IsTestProject calls in CohesionAnalysisService and CouplingAnalysisService; ProjectMetadataParser resolves the complete solution graph for each owner.
- This independent analysis-path performance defect remains separate from the active restore and extraction prerequisites; no batch item is advanced.
