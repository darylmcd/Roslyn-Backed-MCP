# Host Stdio Domain Reference

## Scope

This domain covers MCP host startup, tool/resource/prompt wiring, and protocol-safe logging.

## Primary Location

- `src/Company.RoslynMcp.Host.Stdio/`

## Typical Changes

- Register or adjust tool wrappers.
- Update catalog/resource/prompt wiring.
- Adjust host startup and dependency wiring.

## Validation Focus

- Surface catalog remains coherent.
- Wrapper behavior remains consistent with service contracts.
- Integration tests covering exposed surface continue to pass.
