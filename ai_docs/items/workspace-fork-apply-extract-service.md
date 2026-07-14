# workspace-fork-apply-extract-service — Extract WorkspaceForkApply orchestration into a Core/Roslyn service

**row:** `workspace-fork-apply-extract-service` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:99-217`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:303-322`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:67-73`

## Acceptance

- [ ] WorkspaceForkApply's tool-layer method delegates fork creation/copy/restore/cleanup to a new Core service; cyclomatic complexity of the remaining tool method drops materially.
- [ ] RequireResolvedWorkspaceId exists in exactly one shared location, referenced by both ValidationBundleTools/CompileCheckTools and SymbolTools.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04c-host-build-test-tools::DG1-design, S04c-host-build-test-tools::DG2-cleanliness

## Notes

- During extraction, route `RestoreForkAsync`'s hand-rolled `dotnet restore` spawn through `IDotnetCommandRunner` instead of keeping the duplicated `Process` plumbing (2026-07-14: it needed the same MSBUILDDISABLENODEREUSE + bounded-drain defenses as `DotnetCommandRunner` — see the `dotnet-runner-nodereuse-pipe-hang` changelog entry). The runner hardcodes `FileName = "dotnet"`, so consolidation must carry the `ROSLYNMCP_FORK_DOTNET_PATH` custom-path feature into it (optional executable-path parameter or option).
