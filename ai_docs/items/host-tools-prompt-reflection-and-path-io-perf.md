# host-tools-prompt-reflection-and-path-io-perf — Cache prompt reflection lookup and avoid blocking I/O in path validation

**row:** `host-tools-prompt-reflection-and-path-io-perf` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:78-94`
- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:190-213`

## Acceptance

- [ ] get_prompt_text no longer performs a full assembly reflection scan on every call (lookup built once, reused)
- [ ] ResolvePath's per-parent-directory syscalls no longer block the async continuation thread

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04d-host-workspace-infra-tools::DG4-performance
