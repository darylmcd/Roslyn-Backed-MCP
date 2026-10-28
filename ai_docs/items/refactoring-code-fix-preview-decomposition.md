# refactoring-code-fix-preview-decomposition — Decompose diagnostic code-fix preview orchestration

**row:** `refactoring-code-fix-preview-decomposition` · **pri:** `High` · **size:** `S` · **deps:** `refactoring-format-range-preview-decomposition`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`PreviewCodeFixAsync`)
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`

## Acceptance

- [ ] Extract diagnostic lookup, action selection, changed-solution capture, and preview assembly into named helpers.
- [ ] Keep the orchestrator and each extracted helper below cyclomatic complexity 10 and 80 logical lines.
- [ ] Preserve default-fix selection, explicit fix-id selection, missing diagnostic, and no-action behavior.

## Evidence

- The 2026-08-05 adjacent review measured `PreviewCodeFixAsync` at CC 11/78 logical lines with three nested decision levels.

2026-10-10 source re-derivation at b6fea40ccefc7e967717e4f9f02053ed5a811222: RefactoringService.cs:698-700 attributes fallback to unit-test registry omission, but current production DI and TestServiceContainer build the complete registry graph (ServiceCollectionExtensions.cs:86-87,134; TestServiceContainer.cs:106,131). Re-derive public optional-constructor consumers before any contract change; absence of owned missing-registry construction is not runtime-bug proof. Correct the stale rationale during this orchestration work, preserve genuine no-provider/no-action CS8019 fallback at RefactoringService.cs:702-709, and require ADR plus migration note for a proven necessary public-contract break.
