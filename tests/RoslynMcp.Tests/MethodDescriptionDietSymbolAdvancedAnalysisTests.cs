using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for method-level descriptions on the symbol and advanced-analysis tool
/// families. The shared harness owns reflection; this class owns the type set, measured budget,
/// and distinctions clients need when selecting among similar tools.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietSymbolAdvancedAnalysisTests
{
    private const int MaxDescriptionCharacters = 200;

    // Derived from the measured post-diet text, with modest headroom for a useful future edit.
    private const int MaxAggregateDescriptionCharacters = 6_600;

    private static readonly Type[] SliceToolTypes =
    [
        typeof(SymbolTools),
        typeof(AdvancedAnalysisTools),
        typeof(AnalysisTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] TriggerExpectations =
    [
        new("symbol_search", "wildcards and regex metacharacters are matched literally"),
        new("get_symbol_outline", "Alias for document_symbols"),
        new("find_overrides", "only symbols actually marked `override`"),
        new("find_references_bulk", "up to 50 symbols"),
        new("find_unused_symbols", "zero solution-wide references"),
        new("find_duplicate_helpers", "BCL or NuGet method"),
        new("semantic_search", "not embeddings or vector search"),
        new("project_diagnostics", "compile_check, which is CS-only"),
        new("diagnostic_details", "CA-series NetAnalyzers"),
        new("semantic_grep", "not ripgrep"),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(SliceToolTypes, MaxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            SliceToolTypes, MaxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceTools_AllHaveNonEmptyDescriptions() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(SliceToolTypes);

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(SliceToolTypes, TriggerExpectations);
}
