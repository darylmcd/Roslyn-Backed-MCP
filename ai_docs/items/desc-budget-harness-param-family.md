# desc-budget-harness-param-family — Shared parameter-description canonicalization harness + its two adopters

**row:** `desc-budget-harness-param-family` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ParameterDescriptionBudgetHarness.cs` (new)
- `tests/RoslynMcp.Tests/ParamDescriptionCanonicalFormCodeActionSuppressionTests.cs`
- `tests/RoslynMcp.Tests/ParameterDescriptionCanonicalizationTests.cs`

## Acceptance

- [ ] One shared helper owns the parameter-level `[Description]` reflection enumeration and the canonical-form assertions; each slice declares only its tool-type set and its canonical-form expectations.
- [ ] The helper asserts exact canonical strings for boilerplate parameters rather than shape, and leaves semantically load-bearing parameter descriptions unconstrained — the cold-review advisory both shipped param slices carried.
- [ ] Both existing param slice classes adopt it with unchanged pass/fail behaviour.

## Evidence

`ParameterDescriptionCanonicalizationTests.cs` (193 lines, 12 `typeof`, 2 `[TestMethod]`) and `ParamDescriptionCanonicalFormCodeActionSuppressionTests.cs` (130 lines, 6 `typeof`, 2 `[TestMethod]`) share the parameter enumeration and both assertion bodies. Ten `param-dedupe-*` slice children depend on this helper existing.

## Context

Split from `tool-description-slice-test-harness-consolidation` (2026-09-02).

**Anchor drift corrected at split time.** The parent row's `## Anchors` listed 5 slice test files; the live tree has **10** — 8 method-family (`MethodDescriptionDietDiagnosticsSecurityTests`, `MethodDescriptionDietEditingMsBuildTests`, `MethodDescriptionDietScaffoldingMutationTests`, `MethodDescriptionDietServerSurfaceTests`, `MethodDescriptionDietTestToolingTests`, `RefactoringCoreDescriptionBudgetTests`, `ToolDescriptionDietAnalysisMetricsTests`, `ToolDescriptionDietWorkspaceValidationTests`) and 2 param-family (`ParamDescriptionCanonicalFormCodeActionSuppressionTests`, `ParameterDescriptionCanonicalizationTests`). Ten copies, not five — the parent understated the duplication by half.

**The two families are different enumerations** and need separate helpers: the method family reflects over `[McpServerTool]` methods' own `[Description]`; the param family reflects over those methods' PARAMETERS. Do not force one helper to serve both.

**Why this is the prerequisite.** `method-description-diet` and `param-description-dedupe` were split into 7 + 10 slice children on the same day; each would otherwise spawn another ~98-line copy of this harness, so the duplication grows with every future sweep. All 17 slice children carry a `deps` edge onto this family's helper row.

Measured duplication: a `diff` between two slice files yields 33 differing lines out of 98, with the reflection harness and all three test bodies identical including assert strings.
