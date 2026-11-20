# split-service-with-di-preview-host-hang-triage — Triage split_service_with_di_preview after a host hang

**row:** `split-service-with-di-preview-host-hang-triage` · **pri:** `Medium` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolRefactorTools.cs`

## Acceptance

- [ ] Re-run the two calls under observation (resource monitor, file sink enabled): `SuppressionService` split into two partitions, and a bogus-member partition.
- [ ] Either reproduce and file the root cause, or record a clean run and close this row.

## Evidence

- HANG: no result captured; the tool is `blocked` in the 2026-11-20 scorecard.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
