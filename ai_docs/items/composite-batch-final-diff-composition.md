# composite-batch-final-diff-composition — composite-batch-final-diff-composition

**row:** `composite-batch-final-diff-composition` · **pri:** `Medium` · **size:** `M`

| Field | Value |
|---|---|
| Suggested id | composite-batch-final-diff-composition |
| Priority / size | Medium / M |
| Evidence | src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:98-100 assigns aggregatedDiffs[change.FilePath] = change for each sequential step. Lines127-136 build mutations from initialSolution to final accumulator but return aggregatedDiffs.Values. |
| Regression shape | Two operations edit separate portions of one physical file. Both persist in the final mutation, but the reported diff contains only the second step delta. Neither step must exceed a rendering cap. |
| Distinction | composite-preview-truncation-authority refuses clipped renderings; it does not reconstruct a complete initial-to-final review for multiple untruncated steps. |
| Verification | Static control-flow evidence; no runtime reproduction claimed. |

## Acceptance

- Render each final composite file diff from the original snapshot to the final accumulator, using the same authorized, physically deduplicated mutation plan used for publication.
- Preserve all existing rendering caps and fail-closed truncation issuance. Do not concatenate independently based step hunks or increase limits.
- Preserve ordered operation descriptions, operation validation, source authority, linked-file conflict refusal, and preview-only behavior.
- Test two independent edits in one file: displayed diff includes both, mutation bytes contain both, disk remains unchanged before apply. Observe old behavior fail.
- Cover a later operation reverting an earlier edit (net no-change file), separate-file edits, linked documents, and final aggregate/per-file clipping.

## Advisory anchors

- Production: SymbolRefactorService.PreviewAsync, existing shared SolutionDiffHelper / SolutionFileMutationPlanner (reuse; no duplicate renderer).
- Tests: existing SymbolRefactorService composite preview tests plus focused final-plan rendering controls.
- Parent explicitly requested a separate pre-existing-mechanism row; no implementation included in this payload.
