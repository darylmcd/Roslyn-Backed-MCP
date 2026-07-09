# build-test-services-swallowed-exceptions-no-logging — Add ILogger + stop swallowing exceptions in build/test config services

**row:** `build-test-services-swallowed-exceptions-no-logging` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:133-136`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:518-521`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:1`
- `src/RoslynMcp.Roslyn/Services/SuppressionService.cs:113-118`
- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:1`

## Acceptance

- [ ] Each listed service has an injected ILogger and logs the previously-bare catch blocks with enough context to diagnose the failure.
- [ ] ScaffoldingService's silent fallback to "mstest" on project-parse failure now emits a warning log.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03c-roslyn-build-test-services::DG6-testability-obs
