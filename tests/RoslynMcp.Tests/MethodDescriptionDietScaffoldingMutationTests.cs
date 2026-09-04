using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the scaffolding,
/// project-mutation, multi-file-edit, and cross-project-refactoring tool groups.
/// Deliberately narrower than <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char
/// guard: that constant stays owned by the global ratchet, while these four types are held
/// to a ~200-char capability statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietScaffoldingMutationTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int MaxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice, per the initiative spec. Measured 4,384 chars across
    /// 23 tools before the diet and 2,019 after. <see cref="MaxDescriptionCharacters"/> is a
    /// per-tool CEILING, not a target: most tools in this slice state their capability in well
    /// under 100 chars, so the aggregate is not bounded below by 23 x 200.
    /// </summary>
    private const int MaxAggregateDescriptionCharacters = 2_200;

    private static readonly Type[] SliceToolTypes =
    [
        typeof(ScaffoldingTools),
        typeof(ProjectMutationTools),
        typeof(MultiFileEditTools),
        typeof(CrossProjectRefactoringTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] TriggerExpectations =
    [
        new("scaffold_first_test_file_preview", "Errors when the destination file already exists"),
        new("apply_multi_file_edit", "preview_multi_file_edit + preview_multi_file_edit_apply"),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements()
        => ToolDescriptionBudgetHarness.AssertPerToolBudget(SliceToolTypes, MaxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget()
        => ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            SliceToolTypes, MaxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceTools_AllHaveNonEmptyDescriptions()
        => ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(SliceToolTypes);

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers()
        => ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(SliceToolTypes, TriggerExpectations);
}
