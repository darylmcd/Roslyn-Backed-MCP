using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MethodDescriptionDietUndoWorkspaceLifecycleTests
{
    private const int _maxDescriptionCharacters = 200;
    private const int _maxAggregateDescriptionCharacters = 950;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(UndoTools),
        typeof(WorkspaceWarmTools),
        typeof(ApplyWithVerifyTool),
        typeof(WorkspaceDriftTool),
    ];

    [TestMethod]
    public void SliceToolDescriptions_StayWithinPerToolBudget() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(_sliceToolTypes, _maxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayWithinAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(_sliceToolTypes, _maxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_AreNonEmpty() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(_sliceToolTypes);

    [TestMethod]
    public void SliceToolDescriptions_KeepDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(
            _sliceToolTypes,
            [
                new("revert_last_apply", "SINGLE-SLOT LIFO"),
                new("revert_last_apply", "revert_apply_by_sequence"),
                new("revert_apply_by_sequence", "blocking sequences"),
                new("workspace_warm", "first read-side call"),
                new("apply_with_verify", "auto-revert"),
                new("apply_with_verify", "composite and project-mutation tokens"),
                new("workspace_drift_check", "out-of-band edits"),
                new("workspace_drift_check", "source-generated documents are skipped"),
            ]);
}
