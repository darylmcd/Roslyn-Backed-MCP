# rebase-changed-document-silent-drop — Warn when rebase drops a changed document

**row:** `rebase-changed-document-silent-drop` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs (RebaseChangedDocumentsAsync, the continue on a missing current document)`

## Acceptance

- [ ] A reviewed change whose document vanished from the current solution is refused or reported, never dropped silently; test through ApplyRefactoringAsync.

## Evidence

- Seen while planning PR 1908. A reviewed edit lost without a diagnostic is a correctness hazard for the preview/apply contract.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.

## Notes

### Excluded 2026-11-10 (remediation run, current-major-only)

Release impact UNCERTAIN. The complete fix must refuse (PublicInvalidOperationException, no paths in message, "Create a new preview before applying it") instead of silently dropping a reviewed edit; apply would now fail where it previously reported Success=true with the edit lost. Precedent PR #1811 (project-reference conflict refusal) shipped as Fixed, but docs/release-policy.md does not settle it, and an additive warning is incomplete (ApplyResultDto has no warnings field; the edit is still lost).

Complete scope (RefactoringService.cs, after PR #2055 FindRebaseTargetDocument): throw at RebaseChangedDocumentsAsync missing current document (and the unreachable modifiedDocument-null continue); RebaseAddedDocumentsAsync null doc/FilePath and missing target project; RebaseProjectReferences missing target project and MapReferences dropped references ONLY when the reference participates in affectedTargets/desired set (add test for the unrelated-reference case); null filePath in RebaseRemovedDocuments. Leave removed-document-already-gone alone. Tests in PreviewTokenCrossCouplingTests (sibling delete_file apply then applying an edit preview refuses, disk unchanged, message has no path) plus unit tests; changelog Fixed + Migration sentence.

Trigger to unblock: release-owner decision that the refusal is minor-safe (precedent #1811), or the next major release.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.

Remediation run 2026-11-21: premise holds (RefactoringService.RebaseChangedDocumentsAsync ~:983 skips a missing document/project; ApplyRefactoringAsync returns Success=true with the edit lost). Same-mechanism sites in the rebase helpers: :973-976, :1009-1012, :1015-1018, :1089-1092, :1094-1107 (added references only), :1060-1064 (path-null removal: id-lookup fix, not a refusal). Planned Option B (not implemented): internal PreviewRebaseConflictException caught around PrepareApplyAsync, return Success:false with the existing InvalidPreviewResult() text, invalidate token and post-apply resolver; refusal happens in memory before undo capture and persistence so disk, workspace, undo stack and change log stay untouched. Option A (thrown PublicInvalidOperationException) is major-only. Needs the operator ruling shared with extract-type-static-initialization-order and edit-syntax-check-project-parse-options. Overlap: undo-capture-call-site-cleanup, other-failed-apply-displaces-undo-target, refactoring-project-reference-rebase-complexity touch the same method.
