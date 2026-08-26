# tool-description-slice-test-harness-consolidation — share the per-slice description-budget test harness

## Anchors

- `tests/RoslynMcp.Tests/ToolDescriptionDietAnalysisMetricsTests.cs`
- `tests/RoslynMcp.Tests/ToolDescriptionDietWorkspaceValidationTests.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietScaffoldingMutationTests.cs`
- `tests/RoslynMcp.Tests/RefactoringCoreDescriptionBudgetTests.cs`
- `tests/RoslynMcp.Tests/ParameterDescriptionCanonicalizationTests.cs`

## Acceptance

- [ ] A single shared helper owns the `[McpServerTool]`/`[Description]` reflection enumeration plus the per-tool-ceiling, slice-total and non-empty assertions; each slice test calls it with `(sliceTypes, perToolMax, sliceTotalMax)` and declares only its own constants.
- [ ] Each slice also declares its discriminating-trigger substrings so the shared helper enforces them — the trigger ratchet exists in only some slices today.
- [ ] Remaining `method-description-diet` and `param-description-dedupe` slices adopt the helper instead of copying a new ~98-line file.

## Evidence

Five independent cold code-quality reviews in sweep `20260825T214500Z` raised this same finding, and all five recommended amending the umbrella rows rather than filing siblings. Measured, not estimated: `diff` between two of the slice test files yields 33 differing lines out of 98, with the reflection harness and all three test bodies identical including assert strings.

Both umbrella rows (`method-description-diet`, `param-description-dedupe`) will spawn one more copy of this harness per remaining `Tools/*.cs` slice, so the duplication grows with every future sweep.

## Context

Filed as its own row rather than an amendment because it spans BOTH umbrella rows plus three pre-existing test files; an amendment on either umbrella would leave the other half unowned. Do this before the remaining ~35 slices are swept.
