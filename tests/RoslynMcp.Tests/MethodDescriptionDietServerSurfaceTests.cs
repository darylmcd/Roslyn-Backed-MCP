using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the server/discovery surface
/// (<c>server_info</c>, <c>server_heartbeat</c>, <c>recommend_workflow</c>,
/// <c>suggest_refactorings</c>, <c>get_prompt_text</c>). Deliberately narrower than
/// <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char guard: that constant stays
/// owned by the global ratchet, while these four types are held to a ~200-char capability
/// statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietServerSurfaceTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int MaxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice, per the initiative spec. Measured 2,826 chars across
    /// 5 tools before the diet and 979 after. <see cref="MaxDescriptionCharacters"/> is a
    /// per-tool CEILING, not a target, so the aggregate is not bounded below by 5 x 200; this
    /// constant is the measured post-diet total plus a small drafting headroom.
    /// </summary>
    private const int MaxAggregateDescriptionCharacters = 1_050;

    private static readonly Type[] SliceToolTypes =
    [
        typeof(ServerTools),
        typeof(WorkflowRecommendationTools),
        typeof(SuggestionTools),
        typeof(PromptShimTools),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements()
    {
        var violations = EnumerateSliceTools()
            .Where(entry => entry.Description.Length > MaxDescriptionCharacters)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => $"{entry.Name}: {entry.Description.Length} chars")
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Method [Description] for these tools must be <= {MaxDescriptionCharacters} characters " +
            "(what it does plus the one discriminating trigger); move operational detail to XML " +
            "<remarks>. Violations:\n  " + string.Join("\n  ", violations));
    }

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget()
    {
        var entries = EnumerateSliceTools().ToArray();
        var total = entries.Sum(entry => entry.Description.Length);

        Assert.IsTrue(
            entries.Length > 0,
            "Expected the slice types to declare [McpServerTool] methods; reflection found none.");

        Assert.IsTrue(
            total <= MaxAggregateDescriptionCharacters,
            $"Aggregate method-description budget for the slice is " +
            $"{MaxAggregateDescriptionCharacters} chars across {entries.Length} tools; measured {total}.");
    }

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers()
    {
        // The two clauses most at risk of being dropped rather than relocated.
        AssertDescriptionContains(
            "server_heartbeat",
            "Do not poll waiting for `idle` to self-advance");
        AssertDescriptionContains(
            "server_info",
            "workspace_list is authoritative");

        // Remaining per-tool discriminators.
        AssertDescriptionContains("server_heartbeat", "cheaper than server_info");
        AssertDescriptionContains("suggest_refactorings", "LCOM4");
        AssertDescriptionContains("get_prompt_text", "prompts/get");
        AssertDescriptionContains("recommend_workflow", "requiredWorkspaceState");
    }

    private static void AssertDescriptionContains(string toolName, string expected)
    {
        var entry = EnumerateSliceTools().SingleOrDefault(candidate => candidate.Name == toolName);

        Assert.IsNotNull(entry.Name, $"Tool '{toolName}' was not found on the slice types.");
        StringAssert.Contains(
            entry.Description,
            expected,
            $"Trimming '{toolName}' dropped its discriminating trigger.");
    }

    private static IEnumerable<(string Name, string Description)> EnumerateSliceTools()
        => SliceToolTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => new
            {
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(),
            })
            .Where(entry => entry.Tool is not null && entry.Description is not null)
            .Select(entry => (entry.Tool!.Name ?? string.Empty, entry.Description!.Description));
}
