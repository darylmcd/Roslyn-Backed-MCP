# workspacetools-god-class-decomposition — Split WorkspaceTools.cs into lifecycle + report-builder types

**row:** `workspacetools-god-class-decomposition` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:18`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:703-1097`

## Acceptance

- [ ] WorkspaceTools.cs line count drops materially (target under ~700 lines) after extraction
- [ ] New report-builder type(s) carry unit-level coverage for readiness/support-bundle assembly
- [ ] All existing workspace_* tool tests continue to pass unchanged

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04d-host-workspace-infra-tools::DG1-design, S04d-host-workspace-infra-tools::DG2-cleanliness (WorkspaceTools god-file overlap)
