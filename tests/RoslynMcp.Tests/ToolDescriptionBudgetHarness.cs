using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tests;

/// <summary>
/// Shared reflection harness for the method-level tool-description-budget ratchets. Each
/// consuming slice test class (e.g. <c>ToolDescriptionDietAnalysisMetricsTests</c>,
/// <c>ToolDescriptionDietWorkspaceValidationTests</c>) declares its own swept-type set and
/// per-slice ceilings, then calls into these helpers instead of re-declaring the reflection
/// enumeration and assertion bodies.
/// </summary>
/// <remarks>
/// <para>Mirrors the reflection harness of
/// <c>SurfaceCatalogTests.AllMcpToolMethodDescriptions_AreWithinClientLimit</c>, but enumerates an
/// explicit declaring-type set instead of the whole assembly so sibling diet slices stay
/// independent.</para>
/// <para>Only METHOD-level <c>[Description]</c> attributes are in scope. Parameter-level
/// descriptions and the <c>[McpToolMetadata]</c> summaries the surface catalog mirrors are
/// deliberately excluded.</para>
/// <para>This harness covers the METHOD-family diet only. The PARAMETER-family (reflecting over
/// method parameters) needs its own helper — out of scope here.</para>
/// </remarks>
internal static class ToolDescriptionBudgetHarness
{
    /// <summary>One tool's discriminating-trigger expectation: the substring its trimmed
    /// method-level description must still contain.</summary>
    internal readonly record struct TriggerExpectation(string ToolName, string ExpectedSubstring);

    private readonly record struct DescriptionEntry(string Name, string Description);

    internal static void AssertPerToolBudget(IReadOnlyList<Type> sliceTypes, int maxPerToolDescriptionCharacters)
    {
        var violations = EnumerateSweptToolDescriptions(sliceTypes)
            .Where(entry => entry.Description.Length > maxPerToolDescriptionCharacters)
            .Select(entry => $"{entry.Name}: {entry.Description.Length} chars")
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Swept tool descriptions must be <= {maxPerToolDescriptionCharacters} characters. " +
            "Move operational guidance into XML <remarks> on the same method instead of the wire " +
            "description. Violations:\n  " + string.Join("\n  ", violations));
    }

    internal static void AssertSliceTotalBudget(IReadOnlyList<Type> sliceTypes, int maxSweptSetTotalCharacters)
    {
        var swept = EnumerateSweptToolDescriptions(sliceTypes);
        var total = swept.Sum(entry => entry.Description.Length);

        Assert.IsTrue(
            total <= maxSweptSetTotalCharacters,
            $"Swept-set method description total is {total} chars across {swept.Length} tools, " +
            $"over the {maxSweptSetTotalCharacters}-char slice budget.");
    }

    internal static void AssertAllHaveNonEmptyDescription(IReadOnlyList<Type> sliceTypes)
    {
        var missing = EnumerateSweptToolDescriptions(sliceTypes)
            .Where(entry => string.IsNullOrWhiteSpace(entry.Description))
            .Select(entry => entry.Name)
            .ToArray();

        Assert.AreEqual(
            0,
            missing.Length,
            "Every swept tool must keep a non-empty method-level description so clients can " +
            "discriminate it from sibling tools. Missing:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Asserts each <paramref name="expectations"/> entry's trigger substring is still present in
    /// the named tool's trimmed method-level description. Slices that don't declare a trigger
    /// ratchet simply pass an empty list — this method must never silently no-op a non-empty list.
    /// </summary>
    internal static void AssertDiscriminatingTriggers(
        IReadOnlyList<Type> sliceTypes, IReadOnlyList<TriggerExpectation> expectations)
    {
        if (expectations.Count == 0)
        {
            return;
        }

        var entries = EnumerateSweptToolDescriptions(sliceTypes);

        foreach (var expectation in expectations)
        {
            var entry = entries.SingleOrDefault(candidate => candidate.Name == expectation.ToolName);

            Assert.IsNotNull(entry.Name, $"Tool '{expectation.ToolName}' was not found on the slice types.");
            StringAssert.Contains(
                entry.Description,
                expectation.ExpectedSubstring,
                $"Trimming '{expectation.ToolName}' dropped its discriminating trigger.");
        }
    }

    private static DescriptionEntry[] EnumerateSweptToolDescriptions(IReadOnlyList<Type> sliceTypes) =>
        sliceTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => new
            {
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(),
            })
            .Where(entry => entry.Tool is not null)
            .OrderBy(entry => entry.Tool!.Name, StringComparer.Ordinal)
            .Select(entry => new DescriptionEntry(
                entry.Tool!.Name ?? "(unnamed)",
                entry.Description?.Description ?? string.Empty))
            .ToArray();
}
