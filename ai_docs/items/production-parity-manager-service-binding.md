# production-parity-manager-service-binding — Resolve the registered production manager in wire fixtures

**row:** `production-parity-manager-service-binding` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/ProductionParityMcpHarness.cs:39`: LoadOwnedWorkspaceAsync resolves an unregistered concrete WorkspaceManager.
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:26`: production registers IWorkspaceManager through CreateProviderOwned.
- `tests/RoslynMcp.Tests/EditorConfigArgumentRefusalWireTests.cs`: actual failing wire fixture.

## Acceptance

- [ ] Resolve the production IWorkspaceManager service and retain actual LoadAsync plus owned native globals.
- [ ] Keep production registrations unchanged; no alias registration or replacement manager instance.
- [ ] Audit every reference to the shared preload helper and qualify its affected wire tests on current native artifacts.
- [ ] Preserve wire error shape and unchanged-byte assertions.

## Evidence

- causal20-tests.log: SetEditorConfigOption_NonAssignmentInput_PreservesWireAndNeverMutates fails before the wire assertion with InvalidOperationException: No service for type RoslynMcp.Roslyn.Services.WorkspaceManager has been registered.
- Cold reviewer read the actual production registration and shared helper and approved resolving the registered contract without a production compatibility alias.
