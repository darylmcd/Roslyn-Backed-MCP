# adr-0001-location-migration-corrections — fix ADR 0001's producer survey, null-location semantics, and DTO nullability labels

**row:** `adr-0001-location-migration-corrections` · **pri:** `Medium` · **size:** `M`

## Anchors

- `docs/decisions/0001-locationdto-nested-field-migration.md:20,27,55-56,135`
- `src/RoslynMcp.Roslyn/Services/ScriptingService.cs:248`
- `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:83`
- `src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs:56`
- `src/RoslynMcp.Roslyn/Services/WorkspaceDiagnosticsSink.cs:37`

## Acceptance

- [ ] ADR 0001's producer survey lists all 8 files that construct `DiagnosticDto` (`rg -l "new DiagnosticDto\(" src`), not 5.
- [ ] The ADR's "left null together" claim is corrected — `ScriptingService.cs:253-257` and `SnippetAnalysisService.cs:88-92` construct `DiagnosticDto` with `FilePath: null` but populated line/column, which the current nested `LocationDto.cs:7` (non-nullable `string FilePath`) cannot represent as-is; the ADR must address this shape mismatch explicitly.

## Evidence

- Traced during code-quality review of PR #1180 (`core-dto-location-quartet-consolidation-primary`): grep-verified producer count mismatch (8 vs 5) and a partial-null construction pattern the ADR's stated semantics don't cover.

## Context

Spin-off from writing the LocationDto migration ADR (PR #1180, doc-only initiative). The ADR itself is now merged; these are factual corrections needed before Stage 1 implementation begins against it.
