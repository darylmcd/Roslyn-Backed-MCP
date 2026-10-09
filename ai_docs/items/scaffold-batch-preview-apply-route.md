# scaffold-batch-preview-apply-route — Redeem scaffold tokens in their store

**row:** `scaffold-batch-preview-apply-route` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs:236`
- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs:124-129`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:62-67`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`

## Acceptance

- [ ] A `scaffold_test_batch_preview` token applies through its documented tool, or the preview names the working `preview_multi_file_edit_apply` route.
- [ ] The wrong apply tool identifies the valid route without falsely reporting a workspace reload or consuming the token.
- [ ] Red-first wire test applies the token through the advertised route and verifies the generated files.

## Evidence

- Parent `preview-token-store-mismatch-false-stale`: BatchTestScaffolder stores in `IPreviewStore` while `apply_composite_preview` looks in `ICompositePreviewStore`.

## Context

- Distinct from descriptions of other producers, fork replay, and consumed-token lifecycle.

2026-10-09 source re-vet at 1dd9a62c63e4277ac02ae793415436552ac668a5:
- `BatchTestScaffolder.cs:232-236` computes display changes, then stores through the untagged overload. The proposed changes-aware overload is insufficient for per-file truncation.
- `DiffGenerator.cs:85,98-113` retains the omitted-hunk marker but returns only a string; `SolutionDiffHelper.cs:99,57-64` can accept that short string below the aggregate cap.
- `PreviewStore.cs:166` uses `diffTruncated: ContainsTruncatedSentinel(changes)`; lines169-178 check only `change.FilePath` against `SolutionDiffHelper.TruncatedSentinelFilePath`. Per-file-only omission therefore stores false; `RefactoringService.cs:339-341,356-363` can persist the complete unseen snapshot.
- Require existing `preview-store-explicit-internal-state` before the complete route fix; no marker scanning, safety defaults, dropping interface shim or duplicated generation logic.
- Require existing `tool-refusal-public-message-guard` for its planned inventory asset. Remove only the converted shared-refusal record when that guard is present; re-vet after both predecessors land.
- Same-mechanism test companions: `ExtractionApplyRouteBindingTests.cs:61,166-167`, `PreviewRouteBindingEditingTests.cs:46,106`, `PreviewRouteBindingFileOpsTests.cs:69,156`. Keep exact public refusal assertions and service-not-called checks; preserve BCL mapping-exhaustiveness checks.
- Static source/semantic evidence only; no runtime regression, build or full gate claimed for scaffolding.
