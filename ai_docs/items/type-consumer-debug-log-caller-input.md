# type-consumer-debug-log-caller-input — Keep raw type queries out of operational logs

**row:** `type-consumer-debug-log-caller-input` · **pri:** `Low` · **size:** `S` · **verified:** `a12412bbe4bd31a18cbdd8f481475587e1697c97`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:38`
- `tests/RoslynMcp.Tests/TypeConsumersServiceTests.cs`

## Acceptance

- [ ] Use a synthetic query to verify formatted and structured debug log output. Keep useful workspace and limit context while removing raw caller query text. Preserve successful consumer discovery and the current MCP response contract.

## Evidence

- FindTypeConsumersAsync logs the unvalidated typeName in a named template value. The documented JSON-lines sink preserves named string values. Source-derived.
