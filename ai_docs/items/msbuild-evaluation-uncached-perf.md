# msbuild-evaluation-uncached-perf — Cache MSBuild ProjectCollection evaluation

**row:** `msbuild-evaluation-uncached-perf` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:23-27`
- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:46-49`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:71-113`
- `src/RoslynMcp.Roslyn/Services/SecurityDiagnosticService.cs:143-171`

## Acceptance

- [ ] EvaluatePropertyAsync/EvaluateItemsAsync no longer create a new ProjectCollection+LoadProject on every call for an unchanged project file.
- [ ] NuGetDependencyService and SecurityDiagnosticService share one evaluation/cache path instead of two independent uncached loops.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03c-roslyn-build-test-services::DG4-performance
