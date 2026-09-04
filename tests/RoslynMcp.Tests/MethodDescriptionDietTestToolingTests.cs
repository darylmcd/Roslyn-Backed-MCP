using Microsoft.VisualStudio.TestTools.UnitTesting;
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

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] _triggerExpectations =
    [
        new("test_coverage", "Run tests with code coverage collection"),
        new("test_coverage", "coverlet.collector"),
        new("get_test_coverage_map", "Alias for `test_coverage`"),
        new("get_test_coverage_map", "Prefer `test_coverage`"),
        new("test_reference_map", "statically, without running tests"),
        new("symbol_impact_sweep", "references"),
        new("symbol_impact_sweep", "CS8509/CS8524/IDE0072"),
        new("symbol_impact_sweep", "mapper/converter-suffix callsites"),
        new("preview_record_field_addition", "adding a positional field to a record"),
        new("preview_record_field_addition", "construction, deconstruction, property-pattern, and `with`-expression sites"),
        new("preview_record_field_addition", "does NOT flag"),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(_sliceToolTypes, _maxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            _sliceToolTypes, _maxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceTools_AllHaveNonEmptyDescriptions() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(_sliceToolTypes);

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(_sliceToolTypes, _triggerExpectations);
}
