using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for method descriptions on restructure, syntax, and interface tools.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietRestructureInterfaceTests
{
    private const int MaxDescriptionCharacters = 200;
    private const int MaxAggregateDescriptionCharacters = 900;

    private static readonly Type[] s_sliceToolTypes =
    [
        typeof(RestructureTools),
        typeof(RemoveInterfaceMemberTool),
        typeof(SyntaxTools),
        typeof(InterfaceExtractionTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] s_triggerExpectations =
    [
        new("restructure_preview", "__name__ placeholders"),
        new("replace_string_literals_preview", "magic-string centralization"),
        new("remove_interface_member_preview", "Refuses if a non-implementation caller exists"),
        new("get_syntax_tree", "TruncationNotice"),
        new("extract_interface_preview", "concrete type in the same project"),
        new("extract_interface_preview", "replace concrete-type references"),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(s_sliceToolTypes, MaxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            s_sliceToolTypes, MaxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceTools_AllHaveNonEmptyDescriptions() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(s_sliceToolTypes);

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(s_sliceToolTypes, s_triggerExpectations);
}
