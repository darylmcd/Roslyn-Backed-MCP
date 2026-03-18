# Architecture

## Layering

- `src/Company.RoslynMcp.Host.Stdio/`: MCP host wrapper and registration surface.
- `src/Company.RoslynMcp.Core/`: DTO contracts and cross-layer abstractions.
- `src/Company.RoslynMcp.Roslyn/`: Roslyn-backed workspace and semantic/refactoring services.
- `tests/Company.RoslynMcp.Tests/`: integration and behavior validation.

## Key Boundaries

- Keep transport-specific concerns in host layer.
- Keep public service boundaries DTO-based (no raw Roslyn types crossing boundaries).
- Keep mutation flows preview-first and workspace-version aware.

## Safety Invariants

- Prefer stable tool/resource surface by default.
- Validate with build/tests before merge-ready handoff.
- Update docs/tests when behavior or surface contracts change.

## Deep Material

Historical rationale and superseded investigations belong in `archive/`.
