# workspace-fork-apply-robustness-cancellation — Make WorkspaceForkApply copy/cleanup cancellable and stop TestCoverageTools misreporting cancellation as a timeout

**row:** `workspace-fork-apply-robustness-cancellation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:86-94`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:303-321`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:153-216`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:145-161`

## Acceptance

- [ ] CopyDirectory/DeleteDirectoryIfExists accept and honor a CancellationToken, aborting mid-walk on cancellation.
- [ ] Cancelling a real MCP session mid-test_coverage rethrows OperationCanceledException rather than returning a completed timeout envelope.
- [ ] A second concurrent workspace_fork_apply against the same sourceRoot is rejected or serialized instead of racing the in-flight copy.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04c-host-build-test-tools::DG3-robustness, S04c-host-build-test-tools::DG4-performance
