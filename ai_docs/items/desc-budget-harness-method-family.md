# desc-budget-harness-method-family — Shared method-description budget harness + first two adopters

**row:** `desc-budget-harness-method-family` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ToolDescriptionBudgetHarness.cs` (new)
- `tests/RoslynMcp.Tests/ToolDescriptionDietAnalysisMetricsTests.cs`
- `tests/RoslynMcp.Tests/ToolDescriptionDietWorkspaceValidationTests.cs`

## Acceptance

- [ ] A single shared helper owns the `[McpServerTool]`/`[Description]` reflection enumeration plus the per-tool-ceiling, slice-total and non-empty assertions; a slice test calls it with `(sliceTypes, perToolMax, sliceTotalMax)` and declares only its own constants.
- [ ] The helper also accepts each slice's discriminating-trigger substrings and enforces them — the trigger ratchet exists in only some slices today.
- [ ] The two adopting slice classes shrink to their constants plus the helper call, with identical pass/fail behaviour to today (same ceilings, same totals, same tool sets).

## Evidence

`ToolDescriptionDietAnalysisMetricsTests.cs` (98 lines) and `ToolDescriptionDietWorkspaceValidationTests.cs` (95 lines) are the two closest copies — the pair whose `diff` produced the measured 33-of-98 figure. `ToolDescriptionDietAnalysisMetricsTests.cs:30-31` shows the per-slice knobs (`_maxPerToolDescriptionCharacters = 250`, `_maxSweptSetTotalCharacters = 1_300`) that become the helper's parameters.

## Context

Split from `tool-description-slice-test-harness-consolidation` (2026-09-02).

**Anchor drift corrected at split time.** The parent row's `## Anchors` listed 5 slice test files; the live tree has **10** — 8 method-family (`MethodDescriptionDietDiagnosticsSecurityTests`, `MethodDescriptionDietEditingMsBuildTests`, `MethodDescriptionDietScaffoldingMutationTests`, `MethodDescriptionDietServerSurfaceTests`, `MethodDescriptionDietTestToolingTests`, `RefactoringCoreDescriptionBudgetTests`, `ToolDescriptionDietAnalysisMetricsTests`, `ToolDescriptionDietWorkspaceValidationTests`) and 2 param-family (`ParamDescriptionCanonicalFormCodeActionSuppressionTests`, `ParameterDescriptionCanonicalizationTests`). Ten copies, not five — the parent understated the duplication by half.

**The two families are different enumerations** and need separate helpers: the method family reflects over `[McpServerTool]` methods' own `[Description]`; the param family reflects over those methods' PARAMETERS. Do not force one helper to serve both.

**Why this is the prerequisite.** `method-description-diet` and `param-description-dedupe` were split into 7 + 10 slice children on the same day; each would otherwise spawn another ~98-line copy of this harness, so the duplication grows with every future sweep. All 17 slice children carry a `deps` edge onto this family's helper row.

Measured duplication: a `diff` between two slice files yields 33 differing lines out of 98, with the reflection harness and all three test bodies identical including assert strings.
