# cancellation-swallowed-partial-results-services — cancellation-swallowed-partial-results-services

**row:** `cancellation-swallowed-partial-results-services` · **pri:** `Low` · **size:** `L`

## Anchors
- src/RoslynMcp.Roslyn/Services/DuplicateMethodDetectorService.cs
- src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs
- src/RoslynMcp.Roslyn/Services/NamespaceDependencyService.cs
- src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs
- src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs
- src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs
- src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs
- src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs
- src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs

## Acceptance
Each traversal loop throws OperationCanceledException (ThrowIfCancellationRequested) instead of break/return with partial data, with a cancellation regression test per service.

## Evidence
Same mechanism as the closed consumer-analysis-cancellation-returns-partial; cold review listed `if (ct.IsCancellationRequested) break|return` sites in the files above.
