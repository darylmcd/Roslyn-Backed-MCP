using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-level tool-description diet applied to the workspace,
/// validation, validation-bundle, and compile-check tool groups. The global 2,000-character
/// client-truncation ceiling in <c>SurfaceCatalogTests</c> stays untouched; this class holds the
/// tighter per-slice budget so a future edit cannot silently regrow the <c>tools/list</c> payload.
/// </summary>
/// <remarks>
/// <para>Reflection enumeration and shared assertions live in
/// <see cref="ToolDescriptionBudgetHarness"/>; this class owns only the swept-type set and the
/// per-slice ceilings.</para>
/// <para>Only METHOD-level <c>[Description]</c> attributes are in scope. Parameter-level
/// descriptions and the <c>[McpToolMetadata]</c> summaries the surface catalog mirrors are
/// deliberately excluded.</para>
/// </remarks>
[TestClass]
public sealed class ToolDescriptionDietWorkspaceValidationTests
{
    private const int MaxPerToolDescriptionCharacters = 250;
    private const int MaxSweptSetTotalCharacters = 4_600;

    private static readonly Type[] s_sweptToolTypes =
    [
        typeof(WorkspaceTools),
        typeof(ValidationTools),
        typeof(ValidationBundleTools),
        typeof(CompileCheckTools),
    ];

    [TestMethod]
    public void SweptToolDescriptions_AreWithinPerToolBudget()
        => ToolDescriptionBudgetHarness.AssertPerToolBudget(s_sweptToolTypes, MaxPerToolDescriptionCharacters);

    [TestMethod]
    public void SweptToolDescriptions_TotalIsWithinSliceBudget()
        => ToolDescriptionBudgetHarness.AssertSliceTotalBudget(s_sweptToolTypes, MaxSweptSetTotalCharacters);

    [TestMethod]
    public void SweptTools_AllHaveNonEmptyDescription()
        => ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(s_sweptToolTypes);
}
