using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MethodDescriptionDietRatchetOnlyCleanSlicesTests
{
    private static readonly Type[] _sliceToolTypes =
    [
        typeof(CodeActionTools),
        typeof(FileOperationTools),
        typeof(OrchestrationTools),
        typeof(TypeExtractionTools),
    ];

    [TestMethod]
    public void SliceToolDescriptions_StayWithinPerToolBudget() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(_sliceToolTypes, 200);

    // Measured at 1,499 characters across 15 tools; retain small maintenance headroom.
    [TestMethod]
    public void SliceToolDescriptions_StayWithinAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(_sliceToolTypes, 1_520);

    [TestMethod]
    public void SliceToolDescriptions_AreNonEmpty() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(_sliceToolTypes);

    [TestMethod]
    public void SliceToolDescriptions_KeepDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(
            _sliceToolTypes,
            [
                new("get_code_actions", "position or selection range"),
                new("preview_code_action", "get_code_actions first"),
                new("apply_code_action", "previously previewed code action"),
                new("create_file_preview", "inside a loaded workspace project"),
                new("create_file_apply", "previously previewed file creation"),
                new("delete_file_preview", "deleting an existing source file"),
                new("delete_file_apply", "previously previewed file deletion"),
                new("move_file_preview", "updating its namespace"),
                new("move_file_apply", "previously previewed file move"),
                new("migrate_package_preview", "across all affected projects"),
                new("split_class_preview", "partial class file by moving selected members"),
                new("extract_and_wire_interface_preview", "rewriting DI registrations"),
                new("apply_composite_preview", "DESTRUCTIVE"),
                new("apply_composite_preview", "applies a previously-previewed orchestration operation to disk"),
                new("apply_composite_preview", "in the same session before invoking"),
                new("extract_type_preview", "private field for the new type"),
                new("extract_type_apply", "previously previewed type extraction"),
            ]);
}
