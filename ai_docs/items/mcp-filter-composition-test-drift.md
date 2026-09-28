# mcp-filter-composition-test-drift — share Program.cs's MCP filter composition with the wire-test harnesses

**row:** `mcp-filter-composition-test-drift` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`
- `tests/RoslynMcp.Tests/Helpers/ProductionParityMcpHarness.cs`
- `tests/RoslynMcp.Tests/ResourceReadWireContractTests.cs`
- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`

## Acceptance

- [ ] One host extension on `IMcpServerBuilder` registers the incoming message filter and every request filter that `Program.cs` registers today. That covers `RequestCorrelationMessageFilter`, the four `StaticListResultFilter` list filters, `ResourceReadResultFilter`, `StructuredCallToolFilter`, and `GetPromptErrorFilter`. `Program.cs` and `ProductionParityMcpHarness` both call it, so the helper no longer keeps its own copy.
- [ ] `ResourceReadWireContractTests` claims "Production parity (Program.cs)" but registers only the message filter and `ResourceReadResultFilter`. It builds its server through the shared extension or `ProductionParityMcpHarness`, and its assertions stay green.
- [ ] Each `ServerDiscoveryWireTests` harness that asserts production-visible discovery output uses the shared composition. A harness that deliberately tests a filter subset keeps it and says so in a comment.
- [ ] A test fails if a filter is added to `Program.cs` without going through the shared extension. For example, it can compare the filters registered in production options with those in `ProductionParityMcpHarness` options.

## Evidence

- `ProductionParityMcpHarness.cs` (added on branch `fix/additive-4x-breaking-rework`, PR #1663) copies the filter list from `Program.cs:94-117`.
- About 15 other wire tests compose host services and filter subsets by hand. Examples: `ResourceReadWireContractTests.cs:329-330`, `ServerDiscoveryWireTests.cs:45-52`, `:267-274`, and `:497-501`, `StructuredContentWireContractTests.cs:251-255`, and `WorkspaceResourceListNotificationWireTests.cs:115-123`.
- A new production filter would silently miss every copy. Source: PR #1663 cold review (copy-paste duplication; no defect today).
- Sibling row `test-host-service-provider-composition-dedupe` owns the test-only DI-graph duplication (`AddRoslynMcpHostServices` with its six option objects). This row owns the production-to-test filter drift. Whichever lands first should give the other one helper to build on rather than a second one.

## Context

- The other subset harnesses (`StructuredContentWireContractTests`, `WorkspaceResourceListNotificationWireTests`, `ToolCallErrorWireContractTests`, `ValidationToolsErrorWireTests`, and others) are out of this row's scope. File a follow-up row per test family if any of them turns out to need parity once the shared extension exists.
