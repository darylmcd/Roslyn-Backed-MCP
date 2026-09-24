# compilation-cache-generator-rerun-blinds-unused-analysis — Fix generator-project blindness in unused-symbol, dead-field and coupling analysis

**row:** `compilation-cache-generator-rerun-blinds-unused-analysis` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:61`
- `src/RoslynMcp.Roslyn/Services/SourceGeneratorCompilation.cs:46`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:970`
- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:102`

## Acceptance

- [ ] find_dead_fields on a project with materialized generator output (RoslynMcp.Host.Stdio) no longer reports ServerSurfaceCatalog.s_allTools / PromptParameterIndex.s_index / s_currentReleaseVersion as never-read or safelyRemovable
- [ ] find_unused_symbols no longer reports ServerSurfaceCatalog.Tool (100+ call sites) as unused
- [ ] get_coupling_metrics reports non-zero Ca for Host.Stdio types referenced from other types
- [ ] Regression test loads a fixture project with a source generator and asserts a referenced field is not reported dead

## Evidence

- find_dead_fields(W_RO, projectName=RoslynMcp.Host.Stdio) → s_index/AnalysisTools/s_currentReleaseVersion usageKind=never-read, safelyRemovable=true; all three are read (PromptParameterIndex.cs:32, ServerSurfaceCatalog.cs:32, :105). Introduced by #1402 (92ac5a9c). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- CompilationCache's default factory (SourceGeneratorCompilation.CreateAsync) removes materialized generated trees and reruns the generator driver, returning a Compilation the Solution does not own. Symbols from it passed to SymbolFinder.FindReferencesAsync(symbol, solution) resolve nothing.
- Only projects with materialized generated docs are affected (Host.Stdio has RegexGenerator.g.cs); RoslynMcp.Core is unaffected. Tools that resolve via SymbolResolver (find_references, find_consumers, impact_analysis, symbol_impact_sweep) are unaffected.
- Output feeds remove_dead_code_preview — live code can be deleted by an agent that trusts safelyRemovable. Sibling row find-type-consumers-mutations-generator-blind covers the other two consumers.
