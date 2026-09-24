# find-type-consumers-mutations-generator-blind — Fix find_type_consumers and find_type_mutations empty results in generator projects

**row:** `find-type-consumers-mutations-generator-blind` · **pri:** `High` · **size:** `M` · **deps:** `compilation-cache-generator-rerun-blinds-unused-analysis`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:111`
- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:375`

## Acceptance

- [ ] find_type_consumers(RoslynMcp.Host.Stdio.Catalog.SurfaceEntry) returns the consumers find_references/find_consumers report (61 refs / 13 types)
- [ ] find_type_mutations(HostProcessMetadataStore) reports WriteCurrent with MutationScopes containing IO
- [ ] Regression tests cover a generator-bearing fixture project

## Evidence

- find_type_consumers(SurfaceEntry) → count 0 vs find_references totalCount 61; find_type_mutations(HostProcessMetadataStore) → mutatingMembers [] although WriteCurrent calls File.WriteAllText/Move/Delete. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- TypeConsumersService.ResolveTypeAsync takes the type from ICompilationCache then calls SymbolFinder against the live solution; MutationAnalysisService.ResolveContainingCompilationAsync compares projectCompilation.Assembly (rerun compilation) with namedType.ContainingAssembly (solution) — never equal for generator projects, so SideEffectClassifier is skipped.
- Share the symbol-mapping helper introduced by compilation-cache-generator-rerun-blinds-unused-analysis.
