# apply-with-verify-cancellation-and-compile-scope — Harden ApplyWithVerifyTool's apply→verify→revert flow

**row:** `apply-with-verify-cancellation-and-compile-scope` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:38-118`
- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:47-48`
- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:67-68`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:709`

## Acceptance

- [ ] cancellation firing after a successful apply but before verify/revert completes still triggers a revert attempt with its own guaranteed budget, not the exhausted original token
- [ ] pre- and post-apply compile_check calls pass a ProjectFilter scoped to the affected project(s) instead of CompileCheckOptions()

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04a-host-refactor-tools::DG3-robustness, S04a-host-refactor-tools::DG4-performance
