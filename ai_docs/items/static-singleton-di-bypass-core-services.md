# static-singleton-di-bypass-core-services — Replace static singleton DI-bypass state with scoped services

**row:** `static-singleton-di-bypass-core-services` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Services/WorkspaceEvictedException.cs:147-220`
- `src/RoslynMcp.Core/Services/AmbientGateMetrics.cs:13-49`
- `src/RoslynMcp.Host.Stdio/Diagnostics/SurfaceRegistrationSnapshot.cs:14-23`
- `src/RoslynMcp.Host.Stdio/Diagnostics/SecurityOptionsSnapshot.cs:17-26`
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:116-152`
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:124-343`
- `src/RoslynMcp.Host.Stdio/Program.cs:133-179`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1380-1406`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs:445-534`
- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs:82-111`
- `tests/RoslynMcp.Tests/ServerInfoPathBoundaryTests.cs:147-202`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs:487-601`
- `tests/RoslynMcp.Tests/PreviewApplyBoundaryRevalidationTests.cs:26-34`
- `tests/RoslynMcp.Tests/WorkspaceManagerEvictionTests.cs:51-94`

## Acceptance

- [ ] WorkspaceEvictionRegistry, SurfaceRegistrationSnapshot and SecurityOptionsSnapshot keep no process-wide mutable static state. Each value lives on an instance owned by DI (one per host) and reaches its readers (`WorkspaceManager.cs`, `ServerTools.cs`, `ToolDispatch.cs`) by injection. `SecurityOptionsSnapshot.cs` is deleted: `SecurityOptions` is already a DI singleton (`Program.cs:178`).
- [ ] Apply-time boundary revalidation (`ToolDispatch.RevalidateChangedPathsAsync`) reads `SecurityOptions` from one injected host-singleton boundary service. The service is a required parameter of the `IPreviewStore` overload of `ToolDispatch.ApplyByTokenAsync`, the only caller of the revalidation. It is never an optional or null-default parameter, and never a lookup through the ambient `RequestMcpServerContext`: both would let a missing value skip the security check silently.
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

## Notes

- Size L. Re-derived by the PR #1665 review: up to 22 production files (the 8 anchored, the new boundary service, the 12 `*Tools.cs` call-site files, and WorkspaceEvictionRegistry's own file) and up to 21 test files. Split seam, one static per child:
  1. SecurityOptionsSnapshot and the boundary service. Production (17): `SecurityOptionsSnapshot.cs` (deleted), the new boundary service, `ToolDispatch.cs`, `ServerTools.cs`, `Program.cs`, and the 12 `*Tools.cs` call-site files. Tests (up to 15): the 4 direct `ApplyByTokenAsync` callers, the 5 direct apply-tool callers, and the 6 direct `GetServerInfo` callers (Evidence). Its count derives L, but the 12 `*Tools.cs` edits and most test edits each add one compile-forced argument. Keep it whole and cite that forcing shape in its Scope.
  2. SurfaceRegistrationSnapshot: `SurfaceRegistrationSnapshot.cs`, `ServerTools.cs`, `Program.cs`; test StartupDiagnosticsTests. A new `server_info` parameter also reaches the 6 direct `GetServerInfo` callers. Holding the report on an instance `server_info` already receives avoids that fan-out: PR #1664 does this for the previous-process snapshot on `ServerProcessMetadata`.
  3. WorkspaceEvictionRegistry: `WorkspaceEvictedException.cs`, the registry's own file, `WorkspaceManager.cs`, `Program.cs`; tests HostProcessMetadataTests, WorkspaceManagerEvictionTests. `WorkspaceManager` gains a constructor dependency. Either anchor all 10 direct construction sites (Evidence), or make it an optional parameter that defaults to a non-recycled instance. With the default, add a test proving the DI-resolved manager sees the host's published recycle context, so the default cannot win in production.
  4. AmbientGateMetrics: the inject-or-document decision.
- Shared carrier with `root-expansion-grant-registry-host-ownership`: both rows must reach `RevalidateChangedPathsAsync` from an injected instance. Build one host-singleton boundary service that holds both `SecurityOptions` and the grant state. The first of child 1 and that row to land adds the service and owns the `ApplyByTokenAsync` fan-out. The second adds its state to the existing service and has no fan-out.
- PR #1664 (open at filing) edits comments in `WorkspaceEvictedException.cs` and `Program.cs`, plus `ServerTools.cs` and several of these tests. Re-derive the line numbers after it lands.
