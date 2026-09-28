# static-singleton-di-bypass-core-services — Replace static singleton DI-bypass state with scoped services

**row:** `static-singleton-di-bypass-core-services` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Services/WorkspaceEvictedException.cs:147-220`
- (new) `src/RoslynMcp.Core/Services/WorkspaceEvictionRegistry.cs`
- `src/RoslynMcp.Core/Services/AmbientGateMetrics.cs:13-49`
- `src/RoslynMcp.Core/Models/ServerToolDtos.cs:38-66`
- `src/RoslynMcp.Host.Stdio/Diagnostics/SurfaceRegistrationSnapshot.cs:14-23`
- `src/RoslynMcp.Host.Stdio/Diagnostics/SecurityOptionsSnapshot.cs:17-26`
- (new) `src/RoslynMcp.Host.Stdio/Security/ApplyPathBoundary.cs`
- `src/RoslynMcp.Host.Stdio/Runtime/ServerProcessMetadata.cs`
- `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs:59-102`
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:102-160`
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:124-343`
- `src/RoslynMcp.Host.Stdio/Program.cs:120-179`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:20-32`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1380-1406`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs:445-534`
- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs:82-111`
- `tests/RoslynMcp.Tests/ServerInfoPathBoundaryTests.cs:147-202`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs:487-601`
- `tests/RoslynMcp.Tests/ToolDiResolutionTests.cs:46-192`
- `tests/RoslynMcp.Tests/PreviewApplyBoundaryRevalidationTests.cs:26-34`
- `tests/RoslynMcp.Tests/WorkspaceManagerEvictionTests.cs:51-94`

## Acceptance

- [ ] WorkspaceEvictionRegistry, SurfaceRegistrationSnapshot and SecurityOptionsSnapshot keep no process-wide mutable static state. Each value lives on an instance owned by DI (one per host) and reaches its readers (`WorkspaceManager.cs`, `ServerTools.cs`, `ToolDispatch.cs`) by injection. `SecurityOptionsSnapshot.cs` is deleted: `SecurityOptions` is already a DI singleton (registered by `AddRoslynMcpHostServices`, `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs:73`; `Program.cs:178` resolves it).
- [ ] Apply-time boundary revalidation (`ToolDispatch.RevalidateChangedPathsAsync`) reads `SecurityOptions` from one injected host-singleton boundary service. The service is a required parameter of the `IPreviewStore` overload of `ToolDispatch.ApplyByTokenAsync`, the only caller of the revalidation. It is never an optional or null-default parameter, and never a lookup through the ambient `RequestMcpServerContext`: both would let a missing value skip the security check silently.
- [ ] The service takes `SecurityOptions` as a required constructor argument and exposes it as non-null. The fail-open branch `if (securityOptions is null) return;` in `RevalidateChangedPathsAsync` (`ToolDispatch.cs:321-325`) is deleted. `ToolDispatchTests.ApplyByTokenAsync_NullSecuritySnapshot_SkipsRevalidation_AndApplies` (`ToolDispatchTests.cs:518-555`), which pins that skip, is deleted or replaced by a test proving that a constructed service always revalidates.
- [ ] Each new instance is registered in its composition root, not only in `Program.cs`. The boundary service, and the surface-report holder if it is a new type, go in `AddRoslynMcpHostServices` (`src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`). The eviction registry goes in `AddRoslynServices` (`src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`), and its `WorkspaceManager.CreateProviderOwned` factory passes the registry to the manager. `Program.cs` only fills the registered instances after `builder.Build()`.
- [ ] DI-resolution tests: `ToolDiResolutionTests` builds the tool surface through `AddRoslynMcpHostServices` and proves that no `[McpServerTool]` input schema exposes a parameter of the boundary-service or `SecurityOptions` type. That covers the 19 `*_apply` tools, `server_info`, and any later consumer such as `workspace_load`. A second test resolves `IWorkspaceManager` through `AddRoslynServices` and proves the manager consults the DI-owned registry after a recycle context is published into it.
- [ ] No production-visible static setter or `Reset()` test hook remains; tests construct their own instances instead of saving, overwriting and restoring a global.
- [ ] AmbientGateMetrics becomes an injected service, or keeps its `AsyncLocal` with a source comment recording why per-request ambient flow is the intended design.
- [ ] WorkspaceEvictionRegistry, if it survives as a type, lives in its own file distinct from WorkspaceEvictedException.cs.
- [ ] Each `[DoNotParallelize]` that exists only because of these statics (ToolDispatchTests, ServerInfoPathBoundaryTests, PreviewApplyBoundaryRevalidationTests, WorkspaceManagerEvictionTests) is removed after repeated parallel runs, or kept with a comment naming the remaining non-static dependency.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02b-core-service-contracts::DG7-config-deps-ergo, S02b-core-service-contracts::DG2-cleanliness
- 2026-09-28 cold review of PR #1664 (open at filing), which moves the sibling `HostProcessMetadataSnapshotProvider` static onto a per-instance DI dependency because parallel test classes drained it (the same mechanism). Re-verified at `716b5e20`:
  - `SurfaceRegistrationSnapshot.cs:16` and `SecurityOptionsSnapshot.cs:19` are `private static volatile` fields behind public setters. Production writes each once at startup (`Program.cs:133`, `:179`). `server_info` reads both (`ServerTools.cs:116`, `:152`), and every `*_apply` token redemption reads the security snapshot (`ToolDispatch.cs:321`).
  - Test classes save, overwrite and restore these globals: `StartupDiagnosticsTests.cs:451-532` (3 methods), `ServerInfoPathBoundaryTests.cs:154-201`, `ToolDispatchTests.cs:490-601`, `PreviewApplyBoundaryRevalidationTests.cs`. The assembly runs classes in parallel (`AssemblyInfo.cs:8`, `Parallelize(Scope = ClassLevel)`). Only `[DoNotParallelize]` keeps the security-snapshot writers serialized, and the `ToolDispatchTests.cs:28-37` comment says removing it "is only safe once the snapshot is injected rather than static". The surface-snapshot writers all sit in one parallel-enabled class, and no other test reads `surface.registered`, so that race is latent today.
  - WorkspaceEvictionRegistry is written from the parallel-enabled `HostProcessMetadataTests.AssertEveryStartTimeSurfaceUses` (`:86` publish of a fixed 2026-08-22 start time, `:109` `Reset()`), so parallel classes can observe a fabricated `ServerStartedAtUtc`. `WorkspaceManagerEvictionTests` needs class-level `[DoNotParallelize]` (`:59`; reason at `:51-58`): its published recycle signal would turn parallel classes' `WorkspaceNotFoundException` assertions into `WorkspaceEvictedException`, and a parallel `Reset()` would erase the signal mid-assertion.
  - Anchor drift since filing (`6bdda71a`): WorkspaceEvictionRegistry moved from `WorkspaceEvictedException.cs:151-223` to `:147-220` (#1525), and AmbientGateMetrics from `:12-48` to `:13-49`.
- Injection reach, re-derived at `716b5e20` by the PR #1665 review:
  - `RevalidateChangedPathsAsync` (`ToolDispatch.cs:315-343`) has one caller: the `IPreviewStore` overload of `ApplyByTokenAsync` (`:124-154`). That overload has 19 call sites in 12 `*Tools.cs` files: BulkRefactoring, CodeAction, DeadCode, ExtractMethod, FileOperation (3), FixAll, InterfaceExtraction, MultiFileEdit, Refactoring (5), Scaffolding (2), TypeExtraction, TypeMove. The delegate overload (`:379`), used by OrchestrationTools and ProjectMutationTools, does not revalidate.
  - Tests that call the `IPreviewStore` overload directly: ToolDispatchTests, PreviewApplyBoundaryRevalidationTests, PreviewRouteBindingFileOpsTests, PreviewTokenStaleAcrossAutoReloadTests. Tests that call those 19 apply tool methods directly: ExtractionApplyRouteBindingTests, ParameterObjectPreviewTests, PreviewRouteBindingEditingTests, ScaffoldingIntegrationTests, TypeExtractionTests.
  - Six test files call `ServerTools.GetServerInfo` directly, so a new `server_info` parameter reaches all of them: HostProcessMetadataTests, ServerHeartbeatTests, ServerInfoPathBoundaryTests, ServerInfoUpdateLatestTests, StartupDiagnosticsTests, SurfaceCatalogTests.
  - Ten test files construct `WorkspaceManager` directly: AnalyzerShadowLoaderLifecycleTests, HardeningBehaviorTests, ToolCallErrorWireContractTests, WorkspaceCapLruEvictionTests, WorkspaceEvictionAutoRetryTests, WorkspaceLoadRestoreRaceTests, WorkspaceManagerEvictionTests, WorkspaceReloadedEventTests, WorkspaceSessionLoaderFailureTests, `Workspace/WorkspaceLoadCacheFastPathTests`.
  - `root-expansion-grant-registry-host-ownership` must inject into the same helper: `ToolDispatch.cs:333` reads `RootExpansionGrantRegistry.IsGranted`.
- Composition roots, re-derived at `716b5e20` by the second PR #1665 review:
  - Tool-method service parameters resolve from `AddRoslynMcpHostServices` (`src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs:59`). It registers `SecurityOptions` (`:73`) and `ServerProcessMetadata` (`:84`), then calls `AddRoslynServices` (`:100`). Its remarks (`:48-58`) and `ToolDiResolutionTests.cs:13-27` record the v1.19.0 failure: a type the MCP SDK binder cannot resolve becomes a required user-supplied argument in the tool schema.
  - `ToolDiResolutionTests` builds through the same root (`:175`) but checks only `RoslynMcp.*` interface parameters (`:65-72`, `IsAppServiceInterface` at `:151`), so it would not check a concrete-class carrier. `ToolInputSchemaHygieneTests.EveryTool_InputSchemaProperties_AreSupportedAndDescribed` (`:36`) catches such a leak only indirectly, as an undescribed schema property (`:66`).
  - The only production construction of `WorkspaceManager` is `AddRoslynServices` (`src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:26-32`) calling `WorkspaceManager.CreateProviderOwned` (`WorkspaceManager.cs:145-160`). `TestServiceContainer.cs:105` and `:234` build only through `AddRoslynServices`, so a registry registered at host level would not resolve there.
  - `Program.cs` computes the surface report (`:129`) and the recycle context (`:155-158`) after `builder.Build()` (`:120`). The DI instances must therefore be holders that `Program.cs` fills, not values registered before the build.
  - `ServerToolDtos.cs` remarks name both snapshots (`:44-47` on `PathBoundaryDto`, `:62-66` on `ServerSurfaceCountsDto`) and go stale when they are deleted.

## Notes

- Size L. Re-derived by the second PR #1665 review: up to 26 production files and up to 26 test files. Production: the 12 anchored existing files, the 2 new files (boundary service, WorkspaceEvictionRegistry's own file), and the 12 `*Tools.cs` call-site files. Tests: child 1's 16 and child 3's 10 construction sites, which do not overlap. Split seam, one static per child:
  1. SecurityOptionsSnapshot and the boundary service. Production (19): `SecurityOptionsSnapshot.cs` (deleted), the new boundary service, `ToolDispatch.cs`, `ServerTools.cs`, `Program.cs`, `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs` (registration), `ServerToolDtos.cs` (comment only), and the 12 `*Tools.cs` call-site files. Tests (16): the 4 direct `ApplyByTokenAsync` callers, the 5 direct apply-tool callers, the 6 direct `GetServerInfo` callers (Evidence), and ToolDiResolutionTests (schema test). The count derives L, but the 12 `*Tools.cs` edits and most test edits each add one compile-forced argument. Keep it whole and cite that forcing shape in its Scope.
  2. SurfaceRegistrationSnapshot: `SurfaceRegistrationSnapshot.cs` (deleted), `ServerTools.cs`, `Program.cs`, `ServerToolDtos.cs` (comment only), and the report's holder; test StartupDiagnosticsTests. Hold the report on `ServerProcessMetadata`, which `server_info` already receives. PR #1664 does this for the previous-process snapshot. A new holder type would instead add a registration in `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs` and a new `server_info` parameter reaching the 6 direct `GetServerInfo` callers.
  3. WorkspaceEvictionRegistry: `WorkspaceEvictedException.cs`, the registry's own file, `WorkspaceManager.cs`, `Program.cs`, and `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` (registration in `AddRoslynServices`, plus the `CreateProviderOwned` factory at `:26-32` / `WorkspaceManager.cs:145-160`); tests HostProcessMetadataTests, WorkspaceManagerEvictionTests. `WorkspaceManager` gains a constructor dependency. Either anchor all 10 direct construction sites (Evidence), or make it an optional parameter that defaults to a non-recycled instance. Either way the factory passes the DI-owned registry, and the DI-resolution test (Acceptance) proves the default cannot win in production. That test can live in WorkspaceManagerEvictionTests. `TestServiceContainer.cs` needs no edit when the registry is registered inside `AddRoslynServices`.
  4. AmbientGateMetrics: the inject-or-document decision.
- Shared carrier with `root-expansion-grant-registry-host-ownership`, which depends on this row. Child 1 adds the one host-singleton boundary service (anchored as `Security/ApplyPathBoundary.cs`; the name is illustrative) and owns the `ApplyByTokenAsync` fan-out. That row then moves the grant state onto the same service, with no second signature change. When this row is split, re-point that row's dep from this id to child 1's id. Closing this id without re-pointing would unblock it early.
- PR #1664 (open at filing) edits comments in `WorkspaceEvictedException.cs` and `Program.cs`, plus `ServerTools.cs` and several of these tests. It also removes the `HostProcessMetadataSnapshotProvider.cs` cref to `SurfaceRegistrationSnapshot`; if #1664 has not landed, child 2 must fix that cref too. Re-derive the line numbers after it lands.
- Children 2 and 3 each derive L by one file (5 production files; child 2 includes one comment-only edit). Each is one atomic move: the type, its DI registration or holder, its reader and its writer change together, or DI resolution breaks. Keep each whole and cite that forcing shape in its Scope.
