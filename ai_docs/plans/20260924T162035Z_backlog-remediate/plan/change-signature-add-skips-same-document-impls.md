| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | `RewriteRelatedDeclarationsAsync` (`src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:71`) resolves `accumulator.GetDocument(node.SyntaxTree)` with the ORIGINAL tree and `oldRoot.FindNode(mds.Span)` with ORIGINAL spans against an already-edited document, so after the first edit in a document later declarations are silently missed; caller rewriting likewise undercounts (callsiteUpdates 1 of 5). Confirms the row. |
| Approach | Row Acceptance verbatim: (1) op=add on an interface member whose implementations live in the same file updates every implementation (no CS0535); (2) callsiteUpdates equals find_references callsite count; (3) Regression test: interface + 2 implementations + 3 callers in one file. Regression test first in `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs`. Test files (1): `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 35000 |
| Risks | Fix by grouping declaration (and caller) spans per DocumentId, resolving the document via `solution.GetDocumentId(tree)`, and applying all edits to one root per document (e.g. `ReplaceNodes` / tracked nodes or descending-span order). Check `CollectCallerSpansAsync` consumer for the same shifted-span pattern. Fanout probe: private static helpers, single file; 0 external ripple. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `change_signature_preview` `op=add` now updates every implementation and call site when several live in the same document (previously later ones were skipped, leaving CS0535). |
| Backlog sync | Close rows: [change-signature-add-skips-same-document-impls]. Mark obsolete: []. |
