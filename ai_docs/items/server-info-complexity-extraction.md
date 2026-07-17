# server-info-complexity-extraction — Simplify server_info assembly

**row:** `server-info-complexity-extraction` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:94-192`

## Acceptance

- [ ] `GetServerInfo` measures cyclomatic complexity at or below 8; every new or changed helper is also at or below 8.
- [ ] Focused helpers own version parsing, workspace hint, surface counts, and update-block construction without changing the public DTO or JSON.
- [ ] Preserve the existing two `ListWorkspaces` snapshots, the single consume-once `BuildConnection` call, and latest/status/timestamp read order.
- [ ] All named server-info, heartbeat, startup, metadata, and surface-catalog regressions pass.

## Evidence

- Read-side Roslyn metrics on 2026-07-17 measured `GetServerInfo` at CC 11 and 99 LOC.

## Validation

- Run `ServerInfoUpdateLatestTests`, `ServerHeartbeatTests`, `HostProcessMetadataTests`, `StartupDiagnosticsTests`, and `SurfaceCatalogTests` without modifying them unless a behavior-preserving regression gap is demonstrated.

## Dependencies

- None.
