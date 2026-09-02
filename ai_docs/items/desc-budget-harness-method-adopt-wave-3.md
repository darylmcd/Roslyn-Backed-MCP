# desc-budget-harness-method-adopt-wave-3 — Migrate server-surface, test-tooling and refactoring-core slices

**row:** `desc-budget-harness-method-adopt-wave-3` · **pri:** `Medium` · **size:** `S` · **deps:** `desc-budget-harness-method-family`

## Anchors

- `tests/RoslynMcp.Tests/MethodDescriptionDietServerSurfaceTests.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietTestToolingTests.cs`
- `tests/RoslynMcp.Tests/RefactoringCoreDescriptionBudgetTests.cs`

## Acceptance

- [ ] All three slice classes call the shared harness instead of carrying their own reflection enumeration and assertion bodies.
- [ ] Each declares its own ceilings, tool-type set and discriminating-trigger substrings; no ceiling is loosened during the migration.
- [ ] `RefactoringCoreDescriptionBudgetTests` — the one-type, one-test outlier — either adopts the helper or the row records why the shape does not fit.

## Evidence

The remaining three method-family copies: 111, 121 and 71 lines. `RefactoringCoreDescriptionBudgetTests.cs` is the odd one out (1 `typeof`, 1 `[TestMethod]`) and is the case that proves the helper generalizes.

## Context

Split from `tool-description-slice-test-harness-consolidation` (2026-09-02).

**Anchor drift corrected at split time.** The parent row's `## Anchors` listed 5 slice test files; the live tree has **10** — 8 method-family (`MethodDescriptionDietDiagnosticsSecurityTests`, `MethodDescriptionDietEditingMsBuildTests`, `MethodDescriptionDietScaffoldingMutationTests`, `MethodDescriptionDietServerSurfaceTests`, `MethodDescriptionDietTestToolingTests`, `RefactoringCoreDescriptionBudgetTests`, `ToolDescriptionDietAnalysisMetricsTests`, `ToolDescriptionDietWorkspaceValidationTests`) and 2 param-family (`ParamDescriptionCanonicalFormCodeActionSuppressionTests`, `ParameterDescriptionCanonicalizationTests`). Ten copies, not five — the parent understated the duplication by half.

**The two families are different enumerations** and need separate helpers: the method family reflects over `[McpServerTool]` methods' own `[Description]`; the param family reflects over those methods' PARAMETERS. Do not force one helper to serve both.

**Why this is the prerequisite.** `method-description-diet` and `param-description-dedupe` were split into 7 + 10 slice children on the same day; each would otherwise spawn another ~98-line copy of this harness, so the duplication grows with every future sweep. All 17 slice children carry a `deps` edge onto this family's helper row.

Measured duplication: a `diff` between two slice files yields 33 differing lines out of 98, with the reflection harness and all three test bodies identical including assert strings.
