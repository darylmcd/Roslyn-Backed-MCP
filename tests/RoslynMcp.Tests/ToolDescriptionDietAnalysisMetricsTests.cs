using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-level tool-description diet applied to the cohesion,
/// consumer, coupling, and flow analysis tool groups. The global 2,000-character
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
/// <para>The slice total is derived from measured post-diet content (1,220 characters across the
/// six swept tools), not from <c>toolCount x perToolCeiling</c>. Re-measure and ratchet the
/// constant DOWN if a later edit shrinks the real total.</para>
/// </remarks>
[TestClass]
public sealed class ToolDescriptionDietAnalysisMetricsTests
{
    private const int _maxPerToolDescriptionCharacters = 250;
    private const int _maxSweptSetTotalCharacters = 1_300;

    private static readonly Type[] _sweptToolTypes =
    [
        typeof(CohesionAnalysisTools),
        typeof(ConsumerAnalysisTools),
        typeof(CouplingAnalysisTools),
        typeof(FlowAnalysisTools),
    ];

    [TestMethod]
    public void SweptToolDescriptions_AreWithinPerToolBudget()
        => ToolDescriptionBudgetHarness.AssertPerToolBudget(_sweptToolTypes, _maxPerToolDescriptionCharacters);

    [TestMethod]
    public void SweptToolDescriptions_TotalIsWithinSliceBudget()
        => ToolDescriptionBudgetHarness.AssertSliceTotalBudget(_sweptToolTypes, _maxSweptSetTotalCharacters);

    [TestMethod]
    public void SweptTools_AllHaveNonEmptyDescription()
        => ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(_sweptToolTypes);
}
