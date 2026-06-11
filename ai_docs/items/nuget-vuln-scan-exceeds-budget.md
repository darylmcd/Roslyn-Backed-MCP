# nuget-vuln-scan-exceeds-budget — cache vuln results / heartbeat for the network-bound scan

**row:** `nuget-vuln-scan-exceeds-budget` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs:52`
- security/vuln service under `src/RoslynMcp.Roslyn/Services/`

## Acceptance

- [ ] Per-restore vulnerability result cached keyed on the lock-file hash, and/or a progress heartbeat + documented longer budget surfaced so callers don't treat latency as a hang
- [ ] Regression: fixture asserts a warm second call hits the lock-file-keyed cache (or that a heartbeat is emitted)

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 1, server v2.3.1. Source: 2026-05-31 surface-test.

## Context

`nuget_vulnerability_scan` took 27.6s first-run and 27–106s across repeats on a 7-project solution, exceeding the 15s solution-scan budget (network-bound `dotnet list package --vulnerable`). Functionally correct (0 vulns, all transitive resolved) but slow and variable.
