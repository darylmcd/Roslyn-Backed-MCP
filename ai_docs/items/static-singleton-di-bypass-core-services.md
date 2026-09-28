# static-singleton-di-bypass-core-services — Replace static singleton DI-bypass state with scoped services

**row:** `static-singleton-di-bypass-core-services` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Services/WorkspaceEvictedException.cs:147-220`
- `src/RoslynMcp.Core/Services/AmbientGateMetrics.cs:13-49`
- `src/RoslynMcp.Host.Stdio/Diagnostics/SurfaceRegistrationSnapshot.cs:14-23`
- `src/RoslynMcp.Host.Stdio/Diagnostics/SecurityOptionsSnapshot.cs:17-26`
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:116-152`
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:321`
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

## Notes

- Size L (8 production files, 6 test files, one mechanism). Split seam, one static per child:
  1. SecurityOptionsSnapshot: `SecurityOptionsSnapshot.cs`, `ServerTools.cs`, `ToolDispatch.cs`, `Program.cs`; tests ServerInfoPathBoundaryTests, ToolDispatchTests, PreviewApplyBoundaryRevalidationTests.
  2. SurfaceRegistrationSnapshot: `SurfaceRegistrationSnapshot.cs`, `ServerTools.cs`, `Program.cs`; test StartupDiagnosticsTests.
  3. WorkspaceEvictionRegistry: `WorkspaceEvictedException.cs`, `WorkspaceManager.cs`, `Program.cs`; tests HostProcessMetadataTests, WorkspaceManagerEvictionTests.
  4. AmbientGateMetrics: the inject-or-document decision.
- Related, not folded in: `root-expansion-grant-registry-host-ownership` owns the other static that PreviewApplyBoundaryRevalidationTests serializes on (RootExpansionGrantRegistry).
- PR #1664 (open at filing) edits comments in `WorkspaceEvictedException.cs` and `Program.cs`, plus `ServerTools.cs` and several of these tests. Re-derive the line numbers after it lands.
