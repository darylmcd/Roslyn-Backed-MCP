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
