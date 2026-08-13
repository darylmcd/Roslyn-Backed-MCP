# preview-apply-token-write-path-toctou — token-redeemed writes never revalidate the boundary

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/MultiFileEditTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`

## Acceptance

- [ ] Token redemption revalidates the changed-document paths against the configured root boundary immediately before the physical write, OR the boundary primitive is promoted to a layer both `RoslynMcp.Roslyn` and `RoslynMcp.Host.Stdio` can reference and the canonical target is carried through.
- [ ] One regression proves a preview token cannot be redeemed into a write outside the boundary after a link swap.

## Evidence

Traced during planning of `path-boundary-link-swap-toctou` (Risk 1): `apply_multi_file_edit` and every `*_preview`/`*_apply` token tool routed via `ToolDispatch.ApplyByTokenAsync` -> `RefactoringService.ApplyRefactoringAsync` perform ZERO revalidation at apply time. The validate-to-write window is bounded only by preview-token TTL, making it materially wider than the single-shot window that initiative addressed.

Layering constraint (verified via `.csproj` `ProjectReference` check): `RoslynMcp.Roslyn` references only `RoslynMcp.Core`, so `ConfiguredRootBoundary` (in `Host.Stdio.Security`) is unreachable from `RefactoringService`/`EditService` without a layering violation. Either promote the primitive, or peek and revalidate the `IPreviewStore` entry's changed paths inside `ApplyByTokenAsync` before redeeming.
