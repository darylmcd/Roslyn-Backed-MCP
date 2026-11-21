# change-signature-builder-unused-parameters-and-cancellation — Remove unused builder parameters and thread cancellation

**row:** `change-signature-builder-unused-parameters-and-cancellation` · **pri:** `Low` · **size:** `M` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:1174`
- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:434`

## Acceptance

- [ ] `ValidateCandidateAsync` (`ChangeSignatureAddRemovePreviewBuilder.cs:1174`) drops its unused `original` parameter (caller at `:574`).
- [ ] Probe the rest of the builder for unused parameters on static methods (IDE0060 / analyzer pass) and remove each one; list the hits in the PR.
- [ ] `BoundCallContext`'s constructor (`ChangeSignatureAddRemovePreviewBuilder.cs:36`, `ArgumentOperationResolver.Resolve(..., CancellationToken.None)` at `:48`) and `BulkRefactoringService.RewriteInvocation` (`BulkRefactoringService.cs:434`, `CancellationToken.None`) take the caller's token instead of `CancellationToken.None`.

## Evidence

- Seen during PR 2173 review; the `CancellationToken.None` sites predate PR1's resolver but were copied into it.

## Context

- Overlaps the regions `change-signature-unrelated-invocation-rewrite` PR2 rewrites; sequence after it or rebase carefully.
