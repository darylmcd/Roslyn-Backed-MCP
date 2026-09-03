# analysis-services-hardcoded-parallelism-clamp-magic-numbers — Centralize the duplicated parallelism-clamp and regex-timeout magic numbers in Roslyn analysis services

**row:** `analysis-services-hardcoded-parallelism-clamp-magic-numbers` · **pri:** `Low` · **size:** `M` · **deps:** `bulk-reference-error-detail-redaction,unused-symbol-scan-fail-unsafe-reference-count`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:392`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:162`
- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:611`
- `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs:20`

## Acceptance

- [ ] MutationAnalysisService, UnusedCodeAnalyzer, and ReferenceService source their parallelism clamp bounds from one shared options/config type instead of three separate inline literals.
- [ ] SemanticGrepService's RegexTimeout is configurable per-deployment rather than a hardcoded 2s literal.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03b-roslyn-analysis-services::DG7-config-deps-ergo
