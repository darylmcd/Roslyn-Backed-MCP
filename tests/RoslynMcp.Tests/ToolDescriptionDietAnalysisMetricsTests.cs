using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-level tool-description diet applied to the cohesion,
/// consumer, coupling, and flow analysis tool groups. The global 2,000-character
/// client-truncation ceiling in <c>SurfaceCatalogTests</c> stays untouched; this class holds the
/// tighter per-slice budget so a future edit cannot silently regrow the <c>tools/list</c> payload.
/// </summary>
/// <remarks>
/// <para>Mirrors the reflection harness of
/// <c>SurfaceCatalogTests.AllMcpToolMethodDescriptions_AreWithinClientLimit</c>, but enumerates an
/// explicit declaring-type set instead of the whole assembly so sibling diet slices stay
/// independent and do not collide in this file.</para>
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

    private static (string Name, string Description)[] SweptToolDescriptions() =>
        _sweptToolTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => new
            {
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(),
            })
            .Where(entry => entry.Tool is not null)
            .OrderBy(entry => entry.Tool!.Name, StringComparer.Ordinal)
            .Select(entry => (
                Name: entry.Tool!.Name ?? "(unnamed)",
                Description: entry.Description?.Description ?? string.Empty))
            .ToArray();

    [TestMethod]
    public void SweptToolDescriptions_AreWithinPerToolBudget()
    {
        var violations = SweptToolDescriptions()
            .Where(entry => entry.Description.Length > _maxPerToolDescriptionCharacters)
            .Select(entry => $"{entry.Name}: {entry.Description.Length} chars")
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Swept tool descriptions must be <= {_maxPerToolDescriptionCharacters} characters. " +
            "Move operational guidance into XML <remarks> on the same method instead of the wire " +
            "description. Violations:\n  " + string.Join("\n  ", violations));
    }

    [TestMethod]
    public void SweptToolDescriptions_TotalIsWithinSliceBudget()
    {
        var swept = SweptToolDescriptions();
        var total = swept.Sum(entry => entry.Description.Length);

        Assert.IsTrue(
            total <= _maxSweptSetTotalCharacters,
            $"Swept-set method description total is {total} chars across {swept.Length} tools, " +
            $"over the {_maxSweptSetTotalCharacters}-char slice budget.");
    }

    [TestMethod]
    public void SweptTools_AllHaveNonEmptyDescription()
    {
        var missing = SweptToolDescriptions()
            .Where(entry => string.IsNullOrWhiteSpace(entry.Description))
            .Select(entry => entry.Name)
            .ToArray();

        Assert.AreEqual(
            0,
            missing.Length,
            "Every swept tool must keep a non-empty method-level description so clients can " +
            "discriminate it from sibling tools. Missing:\n  " + string.Join("\n  ", missing));
    }
}
