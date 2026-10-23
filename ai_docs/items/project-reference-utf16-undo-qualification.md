# project-reference-utf16-undo-qualification — Qualify UTF-16 metadata apply and undo after sibling rebase

**row:** `project-reference-utf16-undo-qualification` · **pri:** `Medium` · **size:** `S` · **deps:** `preview-project-reference-metadata-rebase`

## Anchors

- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Use a real isolated MSBuild workspace with a UTF-16 BOM project containing conditional reference metadata, attributes, nested comments and unrelated metadata.
- [ ] Store the metadata preview, perform an unrelated public sibling apply, then redeem the original token; assert exact planned project bytes and surviving sibling disk/workspace text.
- [ ] Undo only the metadata apply; restore the entire original project byte-for-byte, including BOM and metadata/comments, while retaining the earlier sibling edit.
- [ ] Preserve a genuine old-behavior failure receipt; do not claim the fixture's earlier compile/cast failures as defect reproduction.
- [ ] Keep this a test-only qualification slice after its renderer/rebase dependencies. Any newly discovered production defect requires a separately reviewed dependency, not additions to held drafts.
- [ ] Obtain fresh independent review, scoped and hosted checks, and the full serialized delivery gate before closure; verify the test-only PR merge.

## Evidence

- Draft PR1808 commit eea2f083148251f2c66dd09c2693575043388f1b contains ProjectReferenceMetadata_PublicApplyMatchesUtf16Plan_AndUndoRestoresOriginalBytes; extract only this qualification after dependencies land.
- public-sibling-metadata-rebase-red.log: one genuine failure before the rebase correction. renderer-exact-head-scoped.log: 84 combined passes, zero failures/skips; this is not an independent-slice or full-producer result.
- Draft PR1809 is the 23-file scoped checkpoint held at the approximately 2.3x file-count stop trigger.
- Read-only references: origin/codex/restore-reviewed-graph and stash codex-remediate-20261022-preserve-restore; never pop/drop the stash.

## Context

- Drafts: https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1808 and https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1809.
- The original restore row remains open and depends on this qualification chain. No code is copied in the rows-only filing PR.
