# workspace-validation-service-validateinternal-decompose — Decompose WorkspaceValidationService.ValidateInternalAsync

**row:** `workspace-validation-service-validateinternal-decompose` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:163`

## Acceptance

- [ ] ValidateInternalAsync cyclomatic complexity drops below 10 and its parameter list is reduced (e.g. via a parameter object).
- [ ] Existing WorkspaceValidationService tests continue to pass with no behavior change.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03d-roslyn-workspace-infra::DG2-cleanliness
