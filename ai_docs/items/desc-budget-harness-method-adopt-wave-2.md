# desc-budget-harness-method-adopt-wave-2 — Migrate diagnostics/security, editing/MSBuild and scaffolding/mutation slices

**row:** `desc-budget-harness-method-adopt-wave-2` · **pri:** `Medium` · **size:** `S` · **deps:** `desc-budget-harness-method-family`

## Anchors

- `tests/RoslynMcp.Tests/MethodDescriptionDietDiagnosticsSecurityTests.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietEditingMsBuildTests.cs`
- `tests/RoslynMcp.Tests/MethodDescriptionDietScaffoldingMutationTests.cs`

## Acceptance

- [ ] All three slice classes call the shared harness instead of carrying their own reflection enumeration and assertion bodies.
- [ ] Each declares its own ceilings, tool-type set and discriminating-trigger substrings; no ceiling is loosened during the migration.
- [ ] Behaviour is unchanged — the same tools pass and the same violations would fail, proven by the suite staying green with no constant edits.
- [ ] `ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers` gets at least one real caller here (or this row adds a self-test) that exercises both its pass and fail branches — cold review of `desc-budget-harness-method-family` found the method ships with zero callers, so the trigger-ratchet branching is currently unverified.

## Evidence

Three of the eight method-family copies: 115, 130 and 103 lines respectively, each with the same 3 `[TestMethod]`s and 4 `typeof(...)` slice types.

## Context

Split from `tool-description-slice-test-harness-consolidation` (2026-09-02).

**Anchor drift corrected at split time.** The parent row's `## Anchors` listed 5 slice test files; the live tree has **10** — 8 method-family (`MethodDescriptionDietDiagnosticsSecurityTests`, `MethodDescriptionDietEditingMsBuildTests`, `MethodDescriptionDietScaffoldingMutationTests`, `MethodDescriptionDietServerSurfaceTests`, `MethodDescriptionDietTestToolingTests`, `RefactoringCoreDescriptionBudgetTests`, `ToolDescriptionDietAnalysisMetricsTests`, `ToolDescriptionDietWorkspaceValidationTests`) and 2 param-family (`ParamDescriptionCanonicalFormCodeActionSuppressionTests`, `ParameterDescriptionCanonicalizationTests`). Ten copies, not five — the parent understated the duplication by half.

**The two families are different enumerations** and need separate helpers: the method family reflects over `[McpServerTool]` methods' own `[Description]`; the param family reflects over those methods' PARAMETERS. Do not force one helper to serve both.

**Why this is the prerequisite.** `method-description-diet` and `param-description-dedupe` were split into 7 + 10 slice children on the same day; each would otherwise spawn another ~98-line copy of this harness, so the duplication grows with every future sweep. All 17 slice children carry a `deps` edge onto this family's helper row.

Measured duplication: a `diff` between two slice files yields 33 differing lines out of 98, with the reflection harness and all three test bodies identical including assert strings.
