# path-comparison-hardcoded-ignorecase-services — path-comparison-hardcoded-ignorecase-services

**row:** `path-comparison-hardcoded-ignorecase-services` · **pri:** `Low` · **size:** `L`

## Anchors
- src/RoslynMcp.Roslyn/Services/BuildService.cs
- src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs
- src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs
- src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs
- src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs

## Acceptance
Path equality in these services uses FileSystemPath.Comparison, with a Linux case-variant regression test per service.

## Evidence
Cold review of the correctness-15 batch found hardcoded OrdinalIgnoreCase on path comparisons (BuildService ~269, BulkRefactoringService ~309, BatchTestScaffolder ~314 and ~548, CrossProjectRefactoringService ~377, ParameterObjectService EnforceDtoDestinationIsFreeAsync).
