using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the scaffolding,
/// project-mutation, multi-file-edit, and cross-project-refactoring tool groups.
/// Deliberately narrower than <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char
/// guard: that constant stays owned by the global ratchet, while these four types are held
/// to a ~200-char capability statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietScaffoldingMutationTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int MaxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice. Measured 4,384 chars across 23 tools before the diet
    /// and 2,754 after. The floor is not free: the 17 already-compliant tools contribute 1,594
    /// chars on their own, so the theoretical minimum with every outlier at the per-tool
    /// ceiling is ~2,794 - this 2,800 bound is a real ratchet, not slack.
    /// </summary>
    private const int MaxAggregateDescriptionCharacters = 2_800;

    private static readonly Type[] SliceToolTypes =
    [
        typeof(ScaffoldingTools),
        typeof(ProjectMutationTools),
        typeof(MultiFileEditTools),
        typeof(CrossProjectRefactoringTools),
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
        AssertDescriptionContains(
            "scaffold_first_test_file_preview",
            "Errors when the destination file already exists");
        AssertDescriptionContains(
            "apply_multi_file_edit",
            "preview_multi_file_edit + apply_composite_preview");
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
