# first-using-trivia-helper-duplication — Share first-using trivia transformation

**row:** `first-using-trivia-helper-duplication` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:146` (`StripLeadingTriviaFromFirstUsing`)
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:1320` (exact helper copy)
- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveTriviaHelper.cs` (new shared helper if no existing owner fits)
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs` (existing split consumer)
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs` (existing partition consumer)
- `tests/RoslynMcp.Tests/UsingDirectiveTriviaHelperTests.cs` (new bounded helper controls)

## Acceptance

- [ ] Replace both exact StripLeadingTriviaFromFirstUsing implementations with one shared implementation and remove the copies; retain existing caller-specific header policies.
- [ ] Preserve empty, single and multiple-using behavior, all later-using trivia, ordering and source-list immutability; use affected split/partition controls to prove unchanged output.
- [ ] Do not use this mechanical helper to strip generated file headers or declaration XML documentation in moved-file ownership repair; those are separate semantic roles.

## Evidence

- Current primary semantic symbol search returns exactly two method definitions; get_source_text verifies identical ten-line empty/list-first/WithLeadingTrivia(empty)/Replace bodies at ClassSplitOrchestrator146 and SymbolRefactorService1320.
- Both helpers were inspected while seeking a file-header/documentation splitter; their destructive first-using policy is unsuitable for moved-file role separation.
- Independent quality finding; namespace selection/header-role defects remain owned by the selected namespace work. No deduplication repair or additional selected objective is claimed.
