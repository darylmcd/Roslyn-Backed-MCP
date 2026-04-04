# Core Contracts Domain Reference

<!-- purpose: DTOs and cross-layer contracts for the MCP surface. -->

## Scope

This domain covers DTOs, shared abstractions, and cross-layer contract boundaries.

## Primary Location

- `src/RoslynMcp.Core/`

## Typical Changes

- Add or evolve request/response DTOs.
- Adjust service abstraction contracts.
- Refine preview-store and shared boundary models.

## Validation Focus

- Backward/forward compatibility impact reviewed for stable surface.
- Host and Roslyn layers compile against updated contracts.
- Integration tests validate contract behavior where applicable.
