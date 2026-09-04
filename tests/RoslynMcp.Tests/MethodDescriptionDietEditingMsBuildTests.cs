using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the direct-edit,
/// MSBuild-evaluation, pragma-suppression, and composite symbol-refactor tool groups.
/// Deliberately narrower than <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char
/// guard: that constant stays owned by the global ratchet, while these four types are held
/// to a ~200-char capability statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietEditingMsBuildTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int _maxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice, per the initiative spec. Measured 3,770 chars across
    /// 11 tools before the diet and 1,770 after. <see cref="_maxDescriptionCharacters"/> is a
    /// per-tool CEILING, not a target: three tools in this slice already stated their capability
    /// in well under 150 chars, so the aggregate is not bounded below by 11 x 200.
    /// </summary>
    private const int _maxAggregateDescriptionCharacters = 1_850;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(EditTools),
        typeof(MSBuildTools),
        typeof(SuppressionTools),
        typeof(SymbolRefactorTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] _triggerExpectations =
    [
        new("apply_text_edit", "Prefer a semantic preview/apply Roslyn tool whenever one exists"),
        new("apply_text_edit", "Revertible via revert_last_apply"),
        new("pragma_scope_widen", "Refuses when the move would cross a #region/#endregion boundary"),
        new("verify_pragma_suppresses", "'cosmetic pragma' bugs"),
        new("verify_pragma_suppresses", "Read-only"),
        new("record_field_add_with_satellites_preview", "patternDetectionReason"),
        new("symbol_refactor_preview", "a failure in any step aborts the whole preview"),
        new("split_service_with_di_preview", "forwarding facade"),
        new("split_service_with_di_preview", "DI-registration deltas"),
        new("evaluate_msbuild_items", "(e.g. Compile, PackageReference)"),
        new("get_msbuild_properties", "always pass a propertyNameFilter substring or an includedNames allowlist"),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements()
        => ToolDescriptionBudgetHarness.AssertPerToolBudget(_sliceToolTypes, _maxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget()
        => ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            _sliceToolTypes, _maxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceTools_AllHaveNonEmptyDescriptions()
        => ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(_sliceToolTypes);

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers()
        => ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(_sliceToolTypes, _triggerExpectations);
}
