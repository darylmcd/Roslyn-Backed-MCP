using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the refactoring-core tool surface: these six method-level
/// [Description] strings are capability statements, not guidance essays. Refusal reasons
/// live in the services' runtime error messages and budget semantics live in the
/// parameter [Description]s, so the method text must stay short.
/// </summary>
[TestClass]
public sealed class RefactoringCoreDescriptionBudgetTests
{
    private const int MaxDescriptionCharacters = 220;
    private const int MaxAggregateDescriptionCharacters = 1_150;

    private static readonly Type[] SliceToolTypes =
    [
        typeof(ChangeSignatureTools),
        typeof(ExtractMethodTools),
        typeof(SyntaxTools),
        typeof(ParameterObjectTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] TriggerExpectations =
    [
        new("change_signature_preview", "every callsite"),
        new("extract_method_preview", "complete statements"),
        new("extract_method_apply", "previously previewed"),
        new("extract_shared_expression_to_helper_preview", "occurrences < 2"),
        new("get_syntax_tree", "TruncationNotice"),
        new("parameter_object_preview", "Refuses with a reason"),
    ];

    [TestMethod]
    public void RefactoringCoreToolDescriptions_StayWithinPerToolBudget() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(SliceToolTypes, MaxDescriptionCharacters);

    [TestMethod]
    public void RefactoringCoreToolDescriptions_StayWithinAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            SliceToolTypes, MaxAggregateDescriptionCharacters);

    [TestMethod]
    public void RefactoringCoreToolDescriptions_AreNonEmpty() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(SliceToolTypes);

    [TestMethod]
    public void RefactoringCoreToolDescriptions_KeepDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(SliceToolTypes, TriggerExpectations);
}
