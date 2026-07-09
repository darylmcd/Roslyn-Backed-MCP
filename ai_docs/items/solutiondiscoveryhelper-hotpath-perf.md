# solutiondiscoveryhelper-hotpath-perf — Fix hot-path sync I/O and linear scans in SolutionDiscoveryHelper/StructuredCallToolFilter dispatch

**row:** `solutiondiscoveryhelper-hotpath-perf` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs:169-201`
- `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs:104-125`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:466-499`

## Acceptance

- [ ] ScanDirectoriesForSolutions no longer re-walks the full client-root tree unbounded on every read-only dispatch (cached or capped)
- [ ] TryDiscoverFromFilePath's blocking directory walk-up is off the hot synchronous path or made async
- [ ] Workspace-id auto-resolve/recovery lookup against ServerSurfaceCatalog.Tools is O(1) instead of a per-call linear scan

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04e-host-server-infrastructure::DG4-performance
