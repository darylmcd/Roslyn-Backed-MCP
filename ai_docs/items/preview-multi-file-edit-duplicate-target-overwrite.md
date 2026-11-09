# preview-multi-file-edit-duplicate-target-overwrite — Preserve repeated target edits

**row:** `preview-multi-file-edit-duplicate-target-overwrite` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs:334`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:346`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:370`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Choose and enforce one contract when two `FileEditsDto` groups resolve to the same document or physical target, including alternate path spellings: compose against accumulated text with valid coordinates, or reject the batch before issuing a preview token.
- [ ] Observe an old-code failure and fixed-code pass with two edit groups targeting one document. For composition, prove both edits survive in the stored snapshot, visible per-file diffs and token-applied disk text. For refusal, prove there is no preview token or disk write and the refusal gives corrective guidance.
- [ ] Cover both `PreviewMultiFileTextEditsAsync` and `PreviewMultiFileTextEditsOnSolutionAsync`, which share `SimulatePreviewAsync`. Specify whether direct `ApplyMultiFileTextEditsAsync` continues composing repeated targets or adopts the same refusal, and test that public behavior.
- [ ] Correct the stale comments claiming each file's diff sees predecessor rewrites.

## Evidence

- `SimulatePreviewAsync` resolves every source text from `inputSolution` at lines 334-340, builds each merged text from that original at 346-348, then overwrites the accumulating document at 370. It emits both original-to-group diffs; token apply stores only the final snapshot. Direct `ApplyMultiFileTextEditsAsync` reads the current solution each iteration and composes groups. No duplicate resolved-target check exists. Observed during preview-diff remediation on 2026-11-08.

## Context

- Distinct from `preview-diff-whitespace-omission`: that row repairs shared diff comparison. This row repairs repeated-target edit composition or refusal and prevents silent loss of an earlier group from token-applied text.
