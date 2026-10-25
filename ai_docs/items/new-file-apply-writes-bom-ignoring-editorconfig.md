# new-file-apply-writes-bom-ignoring-editorconfig — Preserve intended source encoding through native apply

**row:** `new-file-apply-writes-bom-ignoring-editorconfig` · **pri:** `Low` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1161` — native apply wrapper through1169; replaces stale1031 locator.
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:837` — shared rebase returns unnormalized modified/rebased SourceText through875.
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:227` — string prewrites227–248, native apply283 and project-only reconciliation287–301.
- `src/RoslynMcp.Roslyn/Helpers/SourceFileEncoding.cs:108` — shared null-encoding UTF8 without BOM fallback.

## Acceptance

- [ ] Before implementation, split producer charset/end_of_line selection from shared native-apply null-encoding preservation into independently correct rows; retain every original requirement below.
- [ ] create_file_apply, move_type_to_file_apply and scaffold_*_apply honor .editorconfig charset and end_of_line.
- [ ] All apply paths agree on encoding.
- [ ] Observe RED for actual added and changed source with null SourceText.Encoding; preserve intended physical bytes and resolve the resulting workspace encoding/preamble consistently with the shared no-BOM fallback through final native apply. Retain explicit UTF16 controls.
- [ ] Preserve public producer source bytes, rollback, exact undo, source version and later token redemption; do not change fork bytes to match an unintended native BOM.
- [ ] Re-derive the preserved PR1826 normalization against current main and complete fresh review, scoped and hosted qualification. Do not claim it resolves charset/EOL selection without separate proof.

## Evidence

- Original audit: move_type_to_file_apply produced EF BB BF plus CRLF with charset=utf-8/end_of_line=lf; apply_composite produced no BOM but CRLF. See `ai_docs/audits/20260924-1305/report.md` check C1 and findings.json.
- Current-session `e2-source-encoding-old-causal.log`: real move1FAIL/2PASS/0SKIP; intended Encoding null and shared helper preamble empty, actual final source preamble EFBBBF; explicit UTF16 controls passed. This does not prove general UTF16 loss.
- Immutable PR1826 head0062d60ca47a0b08d56d1c91feac333b67de876a preserves ResolveIntendedDocumentEncodingsAsync on both shared rebase returns. It is unmerged reference work, not evidence on main; PR1828 physical undo capture does not merge this normalization.
- Changed-document OLD controls retained no-BOM physical bytes but returned workspace SourceText.Encoding=null; they prove unresolved resulting encoding metadata, not changed-file BOM loss. See e2-source-normalization-old-causal and e2-source-undo-provenance receipts.
- Receipts and intake: `<user>/.codex/scratch/roslyn-remediate-20261023-split/restore-closeout-independent-intake.md`.

## Context

- Independent nonblocking encoding remainder; restore-callers-missing-packages-path closure does not depend on it.
- Size L reflects the original charset/EOL requirements plus separately observed native-write mechanism; split before code. Do not add a duplicate source-apply-null-encoding-byte-preservation row.
