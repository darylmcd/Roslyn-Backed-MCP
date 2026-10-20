# path-identity-ordinal-ignorecase-services — Replace unconditional OrdinalIgnoreCase file-path identity with FileSystemPath in the remaining services

**row:** `path-identity-ordinal-ignorecase-services` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs:95,205,327,481,511,1203`
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs:95,314,548`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:80,442,598,1091,1101,1460`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:187,192,269` (CompileCheckService is tracked by compile-check-file-scope-path-identity)
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:50,86,343,413,447`
- `src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs:112` (passes an IgnoreCase set into CsprojSemanticEquality, which uses FileSystemPath.Comparer at :239)

## Acceptance

- [ ] Each physical-path identity site in the Anchors uses FileSystemPath.Comparer/Comparison; text-identity uses (diagnostic ids, option keys, UNC prefixes, extensions) stay case-insensitive.
- [ ] Per family a Linux case-distinct regression fails on old code and passes after; Windows insensitive behavior is preserved.
- [ ] Remaining hits from a fresh semantic_grep of OrdinalIgnoreCase in RoslynMcp.Roslyn are classified: zero physical-path identity sites left unlisted. Others in the list: CrossProjectRefactoringService.cs:340,377, DiagnosticDocumentLookup.cs:231, ExtractAndWireOrchestrator.cs:67,99,178, FileOperationService.cs:190, TestReferenceMapService.cs:99,112, NamespaceRelocationService.cs:103, RecordFieldAdditionService.cs:81, RestoreStalenessDetector.cs:84,85,333, TypeMoveService.cs:97.

## Evidence

- Cluster-C sweep (PR for composite-apply-platform-path-identity) found 47 further physical-path OrdinalIgnoreCase sites of the same mechanism outside the fixed functions. Static finding; no runtime reproduction.
