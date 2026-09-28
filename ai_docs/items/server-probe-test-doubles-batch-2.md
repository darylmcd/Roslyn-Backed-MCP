# server-probe-test-doubles-batch-2 — Retire the private server-probe fakes in the metadata, heartbeat and surface-catalog tests (batch 2 of 3)

**row:** `server-probe-test-doubles-batch-2` · **pri:** `Low` · **size:** `S` · **deps:** `server-probe-test-doubles-batch-1`

## Anchors

- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs:90-490`
- `tests/RoslynMcp.Tests/ServerHeartbeatTests.cs:27-209`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs:505-809`

## Acceptance

- [ ] `HostProcessMetadataTests`, `ServerHeartbeatTests` and `SurfaceCatalogTests` delete their private `FakeWorkspaceManager` classes and use the shared `FailClosedWorkspaceManagerStub`. The heartbeat tests pass the workspace statuses their `loadedCount` fake built.
- [ ] `HostProcessMetadataTests` and `ServerHeartbeatTests` delete their private `FakeVersionProvider` classes and use the shared provider from `server-probe-test-doubles-batch-1`.
- [ ] Each converted call site passes the values its old fake produced. No assertion changes, and all three classes keep their test count and pass.

## Evidence

- At `716b5e20`: `HostProcessMetadataTests.cs:462-483` (workspace manager) and `:485-490` (version provider), `ServerHeartbeatTests.cs:27-66` (workspace manager with `loadedCount`) and `:202-209` (version provider), and `SurfaceCatalogTests.cs:787-809` (workspace manager). The full recount is in `server-probe-test-doubles-batch-1`.

## Context

- Batch 2 of 3. It depends on `server-probe-test-doubles-batch-1` for the shared provider.
- PR #1664 (open at filing) rewrites the `server_info` / `server_heartbeat` call sites in all three files. Re-derive the line numbers after it lands.
