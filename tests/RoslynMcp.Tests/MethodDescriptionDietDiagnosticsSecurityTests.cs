using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the exception-flow, security,
/// snippet-analysis, and analyzer-info tool groups. Deliberately narrower than
/// <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char guard: that constant stays owned
/// by the global ratchet, while these four types are held to a ~200-char capability statement
/// with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietDiagnosticsSecurityTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int _maxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice. Measured 3,130 chars across 6 tools before the diet and
    /// 1,079 after (trace_exception_flow 1,012 -> 178, analyze_snippet 682 -> 180,
    /// list_analyzers 640 -> 190, nuget_vulnerability_scan 449 -> 184; security_diagnostics 175
    /// and security_analyzer_status 172 already complied and were left byte-identical).
    /// <see cref="_maxDescriptionCharacters"/> is a per-tool CEILING, not a target, so the
    /// aggregate is not bounded below by 6 x 200 — this constant is the measured post-diet total
    /// plus modest headroom.
    /// </summary>
    private const int _maxAggregateDescriptionCharacters = 1_150;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(ExceptionFlowTools),
        typeof(SecurityTools),
        typeof(SnippetAnalysisTools),
        typeof(AnalyzerInfoTools),
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
        // trace_exception_flow: both handling AND origination sites, and the find_references contrast.
        AssertDescriptionContains("trace_exception_flow", "`throw new` origination sites");
        AssertDescriptionContains("trace_exception_flow", "usage sites, not handling sites");

        // nuget_vulnerability_scan: direct-vs-transitive default and the SDK floor.
        AssertDescriptionContains("nuget_vulnerability_scan", "includeTransitive=false returns direct references only");
        AssertDescriptionContains("nuget_vulnerability_scan", ".NET 8+ SDK");

        // analyze_snippet: ephemeral workspace with no solution load, and the kind-selects-wrapper pointer.
        AssertDescriptionContains("analyze_snippet", "ephemeral workspace");
        AssertDescriptionContains("analyze_snippet", "`kind` selects the wrapper");

        // list_analyzers: the code-fix discovery use case and the flattened-rule pagination unit.
        AssertDescriptionContains("list_analyzers", "code_fix_preview or fix_all_preview");
        AssertDescriptionContains("list_analyzers", "flattened rule list, not whole analyzers");
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
