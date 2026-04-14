using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DiagnosticServiceFilterTotalsTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_TotalsAreInvariantUnderSeverityFilter()
    {
        var unfiltered = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: null, diagnosticIdFilter: null, CancellationToken.None);
        var errorOnly = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Error", diagnosticIdFilter: null, CancellationToken.None);

        // BUG fix (project-diagnostics-filter-totals): the unfiltered totals must be the
        // same regardless of severity filter. Previously errorOnly.TotalWarnings dropped to
        // zero because the filter was applied before counting.
        Assert.AreEqual(unfiltered.TotalErrors, errorOnly.TotalErrors,
            "TotalErrors must be invariant under severity filter.");
        Assert.AreEqual(unfiltered.TotalWarnings, errorOnly.TotalWarnings,
            "TotalWarnings must be invariant under severity filter.");
        Assert.AreEqual(unfiltered.TotalInfo, errorOnly.TotalInfo,
            "TotalInfo must be invariant under severity filter.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_SeverityFilterStillNarrowsReturnedRows()
    {
        var errorOnly = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Error", diagnosticIdFilter: null, CancellationToken.None);

        // The returned rows must respect the filter even though the totals do not.
        Assert.IsTrue(errorOnly.CompilerDiagnostics.All(d => d.Severity == "Error"),
            "Severity filter must still narrow CompilerDiagnostics to Error rows.");
        Assert.IsTrue(errorOnly.AnalyzerDiagnostics.All(d => d.Severity == "Error"),
            "Severity filter must still narrow AnalyzerDiagnostics to Error rows.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_DefaultSeverityFloorIncludesInfoRowsWhenPresent()
    {
        var result = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: null, diagnosticIdFilter: null, CancellationToken.None);

        if (result.TotalInfo > 0)
        {
            var infoReturned =
                result.CompilerDiagnostics.Count(d => string.Equals(d.Severity, "Info", StringComparison.OrdinalIgnoreCase))
                + result.AnalyzerDiagnostics.Count(d => string.Equals(d.Severity, "Info", StringComparison.OrdinalIgnoreCase))
                + result.WorkspaceDiagnostics.Count(d => string.Equals(d.Severity, "Info", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(infoReturned > 0,
                "When totals report Info diagnostics, default (null) severity filter must include Info rows in returned lists.");
        }
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_RepeatCallSameVersion_ReturnsCachedReference()
    {
        // project-diagnostics-large-solution-perf: identical (version, filters) calls hit the
        // result cache on the second invocation. Reference equality is the simplest signal.
        var first = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Warning",
            diagnosticIdFilter: null, CancellationToken.None);
        var second = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Warning",
            diagnosticIdFilter: null, CancellationToken.None);

        Assert.AreSame(first, second,
            "Second call with the same filter set on the same workspace version must return the cached DiagnosticsResultDto.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_DifferentFilters_ReturnsSeparateCachedResults()
    {
        var warningResult = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Warning",
            diagnosticIdFilter: null, CancellationToken.None);
        var errorResult = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Error",
            diagnosticIdFilter: null, CancellationToken.None);

        Assert.AreNotSame(warningResult, errorResult,
            "Different severity filters must produce distinct cache entries (and distinct DTOs).");
    }
}

[TestClass]
public sealed class WorkspaceDiagnosticSeverityClassifierTests
{
    [TestMethod]
    public void Classify_FailureWithPrunedMessage_ReturnsWarning()
    {
        var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
            WorkspaceDiagnosticKind.Failure,
            "PackageReference 'Foo' will not be pruned because the project depends on it.");
        Assert.AreEqual("Warning", severity);
    }

    [TestMethod]
    public void Classify_FailureWithGenericMessage_ReturnsError()
    {
        var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
            WorkspaceDiagnosticKind.Failure,
            "Could not load assembly 'Foo'.");
        Assert.AreEqual("Error", severity);
    }

    [TestMethod]
    public void Classify_WarningKind_ReturnsWarning()
    {
        var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
            WorkspaceDiagnosticKind.Warning,
            "Anything.");
        Assert.AreEqual("Warning", severity);
    }
}
