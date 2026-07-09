# paramvalidation-pagination-upper-bound-clamp — Add enforced upper-bound clamp to ParameterValidation.ValidatePagination

**row:** `paramvalidation-pagination-upper-bound-clamp` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ParameterValidation.cs:50-57`

## Acceptance

- [ ] ValidatePagination throws/clamps when limit exceeds a defined max constant
- [ ] Existing callers compile unchanged (backward-compatible signature or overload)

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04b-host-analysis-tools::DG7-config-deps-ergo
