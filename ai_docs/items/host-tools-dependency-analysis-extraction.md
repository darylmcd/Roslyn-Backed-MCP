# host-tools-dependency-analysis-extraction — Extract dependency-analysis tools

**row:** `host-tools-dependency-analysis-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `complexity-metrics-pagination-validation-hoist,di-registration-scan-completeness,nuget-dependency-scan-completeness,host-assembly-marker-foundation,path-boundary-link-swap-toctou`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` — `GetDiRegistrations`, `GetNamespaceDependencies`, `GetNuGetDependencies`.
- New `src/RoslynMcp.Host.Stdio/Tools/DependencyAnalysisTools.cs`.
- `tests/RoslynMcp.Tests/DiLifetimeOverrideTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`

## Acceptance

- [ ] Move exactly the three anchored attributed endpoints; introduce no forwarding wrapper or duplicate registration.
- [ ] Preserve tool names, descriptions, metadata, parameters/defaults, JSON payloads, and cancellation behavior.
- [ ] Update direct test calls to the new owner.
- [ ] One table-driven raw discovery/behavior matrix proves each name appears exactly once and the existing DI/dependency smoke contracts remain unchanged.

## Evidence

- `AdvancedAnalysisTools` currently mixes 12 unrelated endpoints; this first bounded slice moves the dependency-analysis cluster after DI completeness behavior is fixed.
