# document-and-type-not-found-report-invalid-operation — Document/Type "not found" refusals still report InvalidOperation

**row:** `document-and-type-not-found-report-invalid-operation` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DocumentResolution.cs:85`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:407`
- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:36`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:35`

## Acceptance

- [ ] "Document not found" and "Type ... not found" refusals (about 25 `new InvalidOperationException`/`PublicInvalidOperationException` sites: BulkRefactoring, ClassSplit, CrossProject, ExtractMethod, InterfaceExtraction, SymbolRefactor, TypeExtraction, TypeMove, CodeAction, DocumentResolution) report `NotFound` like the unknown-project and unresolved-symbol cases.
- [ ] `WorkspaceExecutionGate.IsNotFoundError` (post-auto-reload retry) still recognizes them, by type rather than message substring.
- [ ] Wire tests per family in both protocol eras; `Changed - BREAKING` fragment (public repo, contract-care).

## Evidence

- Found while shipping `project-name-not-found-category-next-major`, which fixed only unknown project names and unresolvable rename targets. The gate retry matches the "Document not found" / "not found in workspace" message text on InvalidOperationException, so a blind category change would break the retry.
