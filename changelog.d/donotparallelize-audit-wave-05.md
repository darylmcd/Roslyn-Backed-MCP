---
category: Maintenance
---
- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `CouplingAnalysisTests.cs`, `DeadCodeIntegrationTests.cs`, and `DiagnosticFixIntegrationTests.cs` — removed the opt-out from `CouplingAnalysisTests` after proving it safe with repeated concurrent runs alongside parallel-enabled sibling classes, and documented the two retained opt-outs (`DeadCodeIntegrationTests`, `DiagnosticFixIntegrationTests`) with the concrete shared-state dependency (`RefactoringService.ApplyRefactoringAsync` mutating the assembly-shared `UndoService`/`ChangeTracker` singletons) that still requires serialization.
