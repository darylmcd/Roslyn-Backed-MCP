# Roslyn Services Domain Reference

## Scope

This domain covers workspace loading, diagnostics, semantic navigation, and refactoring behavior.

## Primary Location

- `src/RoslynMcp.Roslyn/`

## Typical Changes

- Update analysis/refactoring implementations.
- Adjust workspace/session behavior.
- Improve diagnostics or semantic data extraction.

## Validation Focus

- Build and tests pass for sample solutions.
- Preview/apply flows remain safe and deterministic.
- No regressions in semantic navigation and diagnostics surfaces.
