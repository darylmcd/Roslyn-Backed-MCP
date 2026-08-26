using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the test-coverage,
/// test-reference-map, symbol-impact-sweep, and record-impact tool groups.
/// Deliberately narrower than <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char
/// guard: that constant stays owned by the global ratchet, while these four types are held
/// to a ~200-char capability statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietTestToolingTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int _maxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice. Measured 1,850 chars across 5 tools before the diet and
    /// 815 after. <see cref="_maxDescriptionCharacters"/> is a per-tool CEILING, not a target: a
    /// 5 x 200 = 1,000 constant would silently license ~185 chars of un-dieted text, so this
    /// bound is the measured post-diet total plus ~9 percent headroom.
    /// </summary>
    private const int _maxAggregateDescriptionCharacters = 890;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(TestCoverageTools),
        typeof(TestReferenceMapTools),
        typeof(ImpactSweepTools),
        typeof(RecordImpactTools),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements()
    {
        var violations = EnumerateSliceTools()
            .Where(entry => entry.Description.Length > _maxDescriptionCharacters)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => $"{entry.Name}: {entry.Description.Length} chars")
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Method [Description] for these tools must be <= {_maxDescriptionCharacters} characters " +
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
            total <= _maxAggregateDescriptionCharacters,
            $"Aggregate method-description budget for the slice is " +
            $"{_maxAggregateDescriptionCharacters} chars across {entries.Length} tools; measured {total}.");
    }

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers()
    {
        // Runtime coverage collection — the discriminator against test_run and test_reference_map.
        AssertDescriptionContains("test_coverage", "Run tests with code coverage collection");
        AssertDescriptionContains("test_coverage", "coverlet.collector");

        // The alias exists only to state its identity and point at the canonical tool.
        AssertDescriptionContains("get_test_coverage_map", "Alias for `test_coverage`");
        AssertDescriptionContains("get_test_coverage_map", "Prefer `test_coverage`");

        // Static-vs-runtime is the only thing separating this from test_coverage in tool search.
        AssertDescriptionContains("test_reference_map", "statically, without running tests");

        // The three result buckets.
        AssertDescriptionContains("symbol_impact_sweep", "references");
        AssertDescriptionContains("symbol_impact_sweep", "CS8509/CS8524/IDE0072");
        AssertDescriptionContains("symbol_impact_sweep", "mapper/converter-suffix callsites");

        // Trigger plus the site categories the response buckets by.
        AssertDescriptionContains(
            "preview_record_field_addition",
            "adding a positional field to a record");
        AssertDescriptionContains(
            "preview_record_field_addition",
            "construction, deconstruction, property-pattern, and `with`-expression sites");
        AssertDescriptionContains("preview_record_field_addition", "does NOT flag");
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
        => _sliceToolTypes
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
