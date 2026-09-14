using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MethodDescriptionDietRefactoringEditorconfigTests
{
    private const int _maxDescriptionCharacters = 200;
    private const int _maxAggregateDescriptionCharacters = 2_150;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(RefactoringTools),
        typeof(EditorConfigTools),
        typeof(DeadCodeTools),
        typeof(TypeMoveTools),
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
                new("rename_preview", "summary=true"),
                new("rename_preview", "high-fan-out"),
                new("format_check", "projectName"),
                new("set_editorconfig_option", "revert_last_apply"),
                new("remove_dead_code_preview", "removeEmptyFiles"),
                new("remove_dead_code_preview", "UX-005"),
                new("move_type_to_file_preview", "at least two top-level types"),
                new("move_type_to_file_preview", "move_file_preview"),
                new("change_type_namespace_preview", "get_namespace_dependencies"),
            ]);
}
