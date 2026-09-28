# server-probe-test-doubles-batch-1 — Server-probe tests each declare private copies of the same workspace-manager and version-provider fakes (batch 1 of 3)

**row:** `server-probe-test-doubles-batch-1` · **pri:** `Low` · **size:** `S`

## Anchors

- (new) `tests/RoslynMcp.Tests/Helpers/FixedLatestVersionProvider.cs`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs:457-614`
- `tests/RoslynMcp.Tests/ServerInfoPathBoundaryTests.cs:160-241`

## Acceptance

- [ ] One shared `ILatestVersionProvider` test double in `tests/RoslynMcp.Tests/Helpers/` takes the latest version, the `VersionCheckStatus` and the last-checked time as explicit inputs, and exposes them as settable properties. It covers all six private variants the three batches retire: fixed `null`/`NeverChecked`, status derived from the latest version, explicit status and time, and the mutable provider.
- [ ] `StartupDiagnosticsTests` and `ServerInfoPathBoundaryTests` delete their private workspace-manager fakes (`StubWorkspaceManager`, `FakeWorkspaceManager`) and version-provider fakes (`StubVersionProvider`, `FakeVersionProvider`). They use the existing `FailClosedWorkspaceManagerStub` and the new provider instead.
- [ ] Each converted call site passes the values its old fake produced. No assertion changes, and both classes keep their test count and pass.

## Evidence

- At `716b5e20`, `server_info` and `server_heartbeat` read only `ListWorkspaces()` from the workspace manager (`ServerTools.cs:57`, `:115`).
- Six test files declare a private list-only `IWorkspaceManager` fake (20-40 lines each; every other member throws or returns `false`/`null`): `HostProcessMetadataTests.cs:462`, `ServerHeartbeatTests.cs:27`, `ServerInfoPathBoundaryTests.cs:210`, `ServerInfoUpdateLatestTests.cs:65`, `StartupDiagnosticsTests.cs:586`, `SurfaceCatalogTests.cs:787`.
- Six test files declare a private `ILatestVersionProvider` fake: `HostProcessMetadataTests.cs:485`, `ServerHeartbeatTests.cs:202`, `ServerInfoPathBoundaryTests.cs:234`, `ServerInfoUpdateLatestTests.cs:22`, `StartupDiagnosticsTests.cs:609`, `ServerInfoUpdateWireContractTests.cs:188`.
- The shared fail-closed double `tests/RoslynMcp.Tests/Helpers/FailClosedWorkspaceManagerStub.cs` already exists (5 test classes use it), and its `params WorkspaceStatusDto[]` list covers every list-only fake. No shared version-provider double exists.
- Found by the cold review of PR #1664 (2026-09-28), which had to touch these private fakes' call sites. That review counted 15 workspace-manager fakes and 5 version-provider fakes. The PR #1665 recount at `716b5e20` replaces those numbers. `tests/` holds 36 `IWorkspaceManager` implementations, and 14 of them are private copies of a shape a shared double covers. Each copy has a row:
  - 6 server-probe list-only fakes: this row and `server-probe-test-doubles-batch-2` / `-batch-3`.
  - 1 list-only copy of `FailClosedWorkspaceManagerStub` (`SamplingMrtrWireTests.cs:1142`): `sampling-mrtr-workspace-fake-retire`.
  - 7 analyzer-family `TestWorkspaceManager` copies: 1 in `unused-code-analyzer-test-harness-wave-1`, 3 in `-wave-2`, 3 in `-wave-3`.
  - The other 22 are the stub itself and 21 doubles that each script scenario-specific members (status, source text, events, versions or gate state). The recount does not count them as copies.
  - All 6 `ILatestVersionProvider` fakes in `tests/` are covered by batches 1-3.
- One shared-double folder: `tests/RoslynMcp.Tests/Helpers/` holds the stub, this row's version provider and the wave-1 analyzer harness. `unused-code-analyzer-test-harness-wave-1` extends the stub rather than adding a second fail-loud double.

## Context

- Batch 1 of 3. It adds the shared provider and converts 2 files. `server-probe-test-doubles-batch-2` and `server-probe-test-doubles-batch-3` convert the other 5 files and depend on this row.
- PR #1664 (open at filing) rewrites the `server_info` / `server_heartbeat` call sites in these files. Re-derive the line numbers after it lands.
