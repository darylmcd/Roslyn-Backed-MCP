# test-host-service-provider-composition-dedupe — Share one host service-provider builder across tests

**row:** `test-host-service-provider-composition-dedupe` · **pri:** `Low` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/HostServiceProviderBuilder.cs` (new)
- `tests/RoslynMcp.Tests/ToolDiResolutionTests.cs:170`
- `tests/RoslynMcp.Tests/StructuredContentWireContractTests.cs:232`
- `tests/RoslynMcp.Tests/WorkspaceResourceListNotificationWireTests.cs:94`
- `tests/RoslynMcp.Tests/TestRunPublicProjectionTests.cs:472`
- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs:478`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs:544`

## Acceptance

- [ ] One shared test helper builds the host DI graph (`AddLogging` + `AddRoslynMcpHostServices` with the six option objects, with an override hook for e.g. `SecurityOptions.SanctionedRoots`) and optionally `AddMcpServer().WithToolsFromAssembly(...)` plus resources/prompts/filters.
- [ ] The anchored call sites use it; per-test differences (server info, request filters, sanctioned roots) are expressed as helper options, not re-copied composition.
- [ ] Adding a new host option type requires editing one test helper, not N copies.
- [ ] Split into 2+ children at routing (size L): helper + first migrations, then the remaining files.

## Evidence

- HEAD 6f31f065 `ToolDiResolutionTests.cs:170-191`: `private static ServiceProvider BuildHostServiceProvider(bool includeMcpTools = false)` → `services.AddRoslynMcpHostServices(new WorkspaceManagerOptions(), new ValidationServiceOptions(), new PreviewStoreOptions(), new ExecutionGateOptions(), new SecurityOptions(), new ScriptingServiceOptions());` then `.AddMcpServer().WithToolsFromAssembly(hostAssembly)`.
- `StructuredContentWireContractTests.cs:232-248`: the same six-option `AddRoslynMcpHostServices(...)` call (with `SanctionedRoots`) followed by `.AddMcpServer(...).WithToolsFromAssembly(hostAssembly)`.
- The same pair recurs at `WorkspaceResourceListNotificationWireTests.cs:94/112`, `TestRunPublicProjectionTests.cs:472/490`, `ServerDiscoveryWireTests.cs:478/494`, `StartupDiagnosticsTests.cs:544/560`.
- The originally cited `ToolInputSchemaHygieneTests.BuildHostServiceProviderWithTools` does not exist at HEAD; the evidence above replaces it. Type: refactor (copy-paste duplication in test infrastructure).

Source: backlog-remediate 20260926T234932Z follow-up.
