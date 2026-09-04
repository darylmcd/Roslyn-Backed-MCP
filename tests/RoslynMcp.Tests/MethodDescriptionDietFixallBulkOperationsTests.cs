using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MethodDescriptionDietFixallBulkOperationsTests
{
    private const int _maxDescriptionCharacters = 200;

    /// <summary>
    /// Measured 3,176 characters across seven tools before the diet and 1,218 after. The
    /// aggregate ratchet leaves 32 characters of maintenance headroom without treating the
    /// per-tool ceiling as a target.
    /// </summary>
    private const int _maxAggregateDescriptionCharacters = 1_250;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(FixAllTools),
        typeof(BulkRefactoringTools),
        typeof(OperationTools),
        typeof(ScriptingTools),
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
                new("fix_all_preview", "per-occurrence code_fix_preview fallback"),
                new("fix_all_apply", "only after fix_all_preview"),
                new("bulk_replace_type_preview", "generic base/interface arguments"),
                new("bulk_replace_type_apply", "bulk_replace_type_preview or replace_invocation_preview"),
                new("replace_invocation_preview", "matching parameter names"),
                new("get_operations", "method identifier or operator token"),
                new("evaluate_csharp", "timeoutSeconds"),
            ]);
}
