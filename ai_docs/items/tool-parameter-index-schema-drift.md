# tool-parameter-index-schema-drift — Derive ToolParameterIndex from the SDK-bound schema, not [Description]

**row:** `tool-parameter-index-schema-drift` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolParameterIndex.cs:90`
- `tests/RoslynMcp.Tests/ToolParameterIndexTests.cs`

## Acceptance

- [ ] ToolParameterIndex's user-facing parameter set for every tool equals the property names of that tool's SDK-generated inputSchema (the MCP SDK binds every non-DI parameter regardless of `[Description]`).
- [ ] A whole-surface test asserts index-vs-schema parity for all registered tools.
- [ ] Consumers (elicitation allowlist, LSP source-location normalizer, schemaHint) keep their behavior for described parameters.

## Evidence

- `ToolParameterIndex.cs:90-94` at 6f31f065: `private static bool IsUserFacing(ParameterInfo parameter) { if (parameter.ParameterType == typeof(CancellationToken)) return false; return parameter.GetCustomAttribute<DescriptionAttribute>() is not null; }` — an undescribed non-DI parameter is bound and advertised by the SDK but invisible to the index (witness: `workspace_close` exposed `getProcessesByName`/`processDrainTimeout`; fixed at the tool in PR #1656, but the index's selection rule is unchanged).
- Consumers: `ElicitationAllowlistPolicy.cs:119,142`, `StructuredCallElicitationCoordinator.cs:62`, `LspSourceLocationArgumentNormalizer.cs:23-25`.

## Context

Surfaced by the unknown-tool-arguments deepener and re-verified by the backlog-remediate 20260926T234932Z spin-off pass; first noted on `workspace-close-schema-leaks-test-seams`, which closes this run, so it is filed standalone.
