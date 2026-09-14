using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for signature-changing and extraction tool descriptions. The reflection
/// harness keeps these method-level capability statements compact while preserving the phrases
/// that let clients distinguish preview routes and refusal behavior.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietSignatureExtractionTests
{
    private const int _maxDescriptionCharacters = 200;

    // Measured at 819 chars after the three over-ceiling descriptions were trimmed; the ceiling is not a target.
    private const int _maxAggregateDescriptionCharacters = 850;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(ParameterObjectTools),
        typeof(ChangeSignatureTools),
        typeof(ExtractMethodTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] _triggerExpectations =
    [
        new("parameter_object_preview", "Refuses with a reason rather than partially proceeding"),
        new("parameter_object_preview", "apply_with_verify"),
        new("change_signature_preview", "every callsite"),
        new("change_signature_preview", "one preview token"),
        new("extract_method_preview", "complete statements in one block"),
        new("extract_method_preview", "no return statements"),
        new("extract_method_apply", "previously previewed extract method refactoring"),
        new("extract_shared_expression_to_helper_preview", "unlike extract_method_preview"),
        new("extract_shared_expression_to_helper_preview", "occurrences < 2"),
        new("extract_shared_expression_to_helper_preview", "mixed free-variable types"),
        new("extract_shared_expression_to_helper_preview", "cross-type hits when allowCrossFile=false"),
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
