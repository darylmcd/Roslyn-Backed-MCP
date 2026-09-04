using Microsoft.VisualStudio.TestTools.UnitTesting;
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

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] _triggerExpectations =
    [
        // trace_exception_flow: both handling AND origination sites, and the find_references contrast.
        new("trace_exception_flow", "`throw new` origination sites"),
        new("trace_exception_flow", "usage sites, not handling sites"),

        // nuget_vulnerability_scan: direct-vs-transitive default and the SDK floor.
        new("nuget_vulnerability_scan", "includeTransitive=false returns direct references only"),
        new("nuget_vulnerability_scan", ".NET 8+ SDK"),

        // analyze_snippet: ephemeral workspace with no solution load, and the kind-selects-wrapper pointer.
        new("analyze_snippet", "ephemeral workspace"),
        new("analyze_snippet", "`kind` selects the wrapper"),

        // list_analyzers: the code-fix discovery use case and flattened-rule pagination unit.
        new("list_analyzers", "code_fix_preview or fix_all_preview"),
        new("list_analyzers", "flattened rule list, not whole analyzers"),
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

    [TestMethod]
    public void DiscriminatingTriggerHarness_RejectsMissingSubstring()
    {
        ToolDescriptionBudgetHarness.TriggerExpectation[] missingTrigger =
        [
            new("trace_exception_flow", "__missing_discriminating_trigger__"),
        ];

        Assert.ThrowsExactly<AssertFailedException>(() =>
            ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(_sliceToolTypes, missingTrigger));
    }
}
