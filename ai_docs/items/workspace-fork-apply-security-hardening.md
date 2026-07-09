# workspace-fork-apply-security-hardening — Exclude secret-bearing files from workspace_fork_apply copies, expire kept forks, and clean up coverage temp dirs

**row:** `workspace-fork-apply-security-hardening` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:25-35`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:111`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:331-345`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:60`

## Acceptance

- [ ] DirectoryCopyExclusions skips known secret-file patterns (.env, appsettings.*.json, *.pubxml, secrets.json) before CopyDirectory runs.
- [ ] retention=keep forks have a documented expiry/cleanup path instead of persisting indefinitely.
- [ ] test_coverage deletes its temp coverage directory after aggregation completes; RestoreForkAsync's dotnet path and restore timeout are configurable.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04c-host-build-test-tools::DG5-security-data, S04c-host-build-test-tools::DG7-config-deps-ergo
