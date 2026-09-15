using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[DoNotParallelize]
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
    public async Task ProjectDiagnostics_SummaryOrdersSeverityThenDescendingCountAsync()
    {
        var json = await AnalysisTools.GetProjectDiagnostics(WorkspaceExecutionGate,
            new MixedSeverityDiagnosticService(), WorkspaceId, summary: true, ct: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var groups = document.RootElement.GetProperty("diagnosticGroups").EnumerateArray().ToArray();
        Assert.AreSequenceEqual(new[] { "E2", "E1", "W2", "W1", "I2", "I1" },
            groups.Select(group => group.GetProperty("id").GetString()).ToArray());
        Assert.AreSequenceEqual(new[] { 2, 1, 2, 1, 2, 1 },
            groups.Select(group => group.GetProperty("count").GetInt32()).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectDiagnostics_SameIdAcrossSeverities_PreservesCountsAsync(bool reverse)
    {
        var json = await AnalysisTools.GetProjectDiagnostics(WorkspaceExecutionGate,
            new MixedSeverityDiagnosticService(sameId: true, reverse), WorkspaceId,
            summary: true, offset: 8, limit: 1, ct: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var groups = root.GetProperty("diagnosticGroups").EnumerateArray().ToArray();

        Assert.AreEqual(1, root.GetProperty("distinctDiagnosticIds").GetInt32());
        Assert.AreEqual(9, root.GetProperty("filteredDiagnostics").GetInt32());
        Assert.AreSequenceEqual(new[] { "Error", "Warning", "Info" },
            groups.Select(group => group.GetProperty("severity").GetString()).ToArray());
        Assert.IsTrue(groups.All(group => group.GetProperty("id").GetString() == "TEST001"));
        Assert.IsTrue(groups.All(group => group.GetProperty("count").GetInt32() == 3));
        Assert.IsTrue(groups.All(group => group.GetProperty("category").GetString() == "Testing"));
        AssertTotalDiagnosticsMatchesSumOfSeverityTotals(root, "mixed severity for one ID");
    }

    private sealed class MixedSeverityDiagnosticService(bool sameId = false, bool reverse = false) : IDiagnosticService
    {
        public Task<DiagnosticsResultDto> GetDiagnosticsAsync(string workspaceId,
            string? projectFilter, string? fileFilter, string? severityFilter,
            string? diagnosticIdFilter, CancellationToken ct)
        {
            List<DiagnosticDto> diagnostics = [];
            foreach (var (prefix, severity) in new[] { ("I", "Info"), ("W", "Warning"), ("E", "Error") })
            {
                diagnostics.Add(new DiagnosticDto(sameId ? "TEST001" : prefix + "1", "Probe", severity, "Testing", null, null, null, null, null));
                var duplicate = new DiagnosticDto(sameId ? "TEST001" : prefix + "2", "Probe", severity, "Testing", null, null, null, null, null);
                diagnostics.AddRange([duplicate, duplicate]);
            }
            if (reverse) diagnostics.Reverse();
            return Task.FromResult(new DiagnosticsResultDto([], diagnostics, [], 3, 3, 3));
        }

        public Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(string workspaceId,
            string diagnosticId, string filePath, int line, int column, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectDiagnostics_InfoOnlyScopeWithWarningFloor_ReportsZeroFilteredTotal(bool summary)
    {
        var json = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate, new InfoOnlyDiagnosticService(), WorkspaceId,
            severity: "Warning", summary: summary, ct: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(3, document.RootElement.GetProperty("totalInfo").GetInt32());
        Assert.AreEqual(3, document.RootElement.GetProperty("totalDiagnostics").GetInt32());
        Assert.AreEqual(0, document.RootElement.GetProperty("filteredDiagnostics").GetInt32());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectDiagnostics_FilteredTotalMatchesServiceBeforePagination(bool summary)
    {
        var expected = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, null, null, "Warning", "CS0414", CancellationToken.None);
        var expectedCount = expected.WorkspaceDiagnostics.Count + expected.CompilerDiagnostics.Count
            + expected.AnalyzerDiagnostics.Count;
        Assert.IsGreaterThan(0, expectedCount, "The sample must exercise a nonempty filtered result.");
        var json = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate, DiagnosticService, WorkspaceId,
            severity: "Warning", diagnosticId: "CS0414", offset: expectedCount, limit: 1,
            summary: summary, ct: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(expectedCount, document.RootElement.GetProperty("filteredDiagnostics").GetInt32());
        if (!summary) Assert.AreEqual(0, document.RootElement.GetProperty("returnedDiagnostics").GetInt32());
    }

    // Exercise the tool's wire projection with a deterministic Info-only service response.
    // The real-service test above owns filter and pagination integration.
    private sealed class InfoOnlyDiagnosticService : IDiagnosticService
    {
        public Task<DiagnosticsResultDto> GetDiagnosticsAsync(string workspaceId,
            string? projectFilter, string? fileFilter, string? severityFilter,
            string? diagnosticIdFilter, CancellationToken ct)
        {
            Assert.AreEqual("Warning", severityFilter);
            return Task.FromResult(new DiagnosticsResultDto([], [], [], 0, 0, 3));
        }

        public Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(string workspaceId,
            string diagnosticId, string filePath, int line, int column, CancellationToken ct) =>
            throw new NotSupportedException();
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

    [TestMethod]
    [DataRow("unfiltered")]
    [DataRow("project")]
    [DataRow("file")]
    [DataRow("severity")]
    [DataRow("diagnostic-id")]
    [DataRow("combined")]
    public async Task DiagnosticQueryService_CachedAndUncachedFilters_PreserveResultContract(
        string scenario)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var project = solution.Projects.Single(candidate => candidate.Name == "SampleLib");
        var diagnosticFile = project.Documents.Single(document =>
            document.Name == "DiagnosticsProbe.cs").FilePath!;
        var projectFilter = scenario is "project" or "combined" ? project.Name : null;
        var fileFilter = scenario is "file" or "combined" ? diagnosticFile : null;
        var severityFilter = scenario is "severity" or "combined" ? "Error" : null;
        var diagnosticIdFilter = scenario is "diagnostic-id" or "combined" ? "CS0414" : null;

        using var compilationCache = new CompilationCache(WorkspaceManager);
        var queryService = new DiagnosticQueryService(WorkspaceManager, compilationCache);
        var filters = new DiagnosticQueryFilters(
            projectFilter,
            fileFilter,
            severityFilter,
            diagnosticIdFilter);
        var uncached = await queryService.GetDiagnosticsAsync(
            WorkspaceId,
            filters,
            CancellationToken.None);
        var cached = await queryService.GetDiagnosticsAsync(
            WorkspaceId,
            filters,
            CancellationToken.None);

        Assert.AreSame(
            uncached,
            cached,
            $"The second {scenario} query must use the version-and-filter cache entry.");
        AssertEquivalentResultContract(uncached, cached, scenario);
        AssertFiltersApplied(
            uncached,
            fileFilter,
            severityFilter,
            diagnosticIdFilter,
            scenario);
    }

    [TestMethod]
    public async Task TryGetCachedWorkspaceDiagnostics_ReturnsCurrentFullWorkspaceTotalsWithoutAnalyzerPass()
    {
        var analyzed = await DiagnosticService.GetDiagnosticsAsync(
            WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Error",
            diagnosticIdFilter: null, CancellationToken.None);

        var cacheHit = DiagnosticService.TryGetCachedWorkspaceDiagnostics(WorkspaceId, out var cached);

        Assert.IsTrue(cacheHit);
        Assert.IsNotNull(cached);
        Assert.AreEqual(analyzed.TotalErrors, cached.TotalErrors);
        Assert.AreEqual(analyzed.TotalWarnings, cached.TotalWarnings);
        Assert.AreEqual(analyzed.TotalInfo, cached.TotalInfo,
            "The cache-only support-bundle path must reuse current full-workspace totals.");
    }

    [TestMethod]
    public async Task ProjectDiagnostics_TotalDiagnosticsIsInvariantUnderSeverityFilter()
    {
        // BUG fix (project-diagnostics-totaldiagnostics-collapses-under-severity-filter):
        // The tool-layer `totalDiagnostics` field previously counted the severity-filtered
        // page (`allDiagnostics.Count`), so `severity=Error` collapsed `totalDiagnostics` to
        // the error count even though `totalErrors`/`totalWarnings`/`totalInfo` reported the
        // full scope. After the fix, `totalDiagnostics` must equal the sum of the per-severity
        // totals and match between `severity=null` and `severity=Error` calls on the same
        // workspace. Asserts both the default (paged) branch and the `summary=true` branch.
        var unfilteredJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            projectName: null,
            file: null,
            severity: null,
            diagnosticId: null,
            offset: 0,
            limit: 1,
            summary: false,
            progress: null,
            ct: CancellationToken.None);
        var errorOnlyJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            projectName: null,
            file: null,
            severity: "Error",
            diagnosticId: null,
            offset: 0,
            limit: 1,
            summary: false,
            progress: null,
            ct: CancellationToken.None);

        using var unfilteredDoc = JsonDocument.Parse(unfilteredJson);
        using var errorOnlyDoc = JsonDocument.Parse(errorOnlyJson);

        AssertTotalDiagnosticsMatchesSumOfSeverityTotals(unfilteredDoc.RootElement, "severity=null (default branch)");
        AssertTotalDiagnosticsMatchesSumOfSeverityTotals(errorOnlyDoc.RootElement, "severity=Error (default branch)");

        Assert.AreEqual(
            unfilteredDoc.RootElement.GetProperty("totalDiagnostics").GetInt32(),
            errorOnlyDoc.RootElement.GetProperty("totalDiagnostics").GetInt32(),
            "totalDiagnostics must be invariant under severity filter (default branch).");

        // Also assert summary=true branch.
        var unfilteredSummaryJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            projectName: null,
            file: null,
            severity: null,
            diagnosticId: null,
            offset: 0,
            limit: 1,
            summary: true,
            progress: null,
            ct: CancellationToken.None);
        var errorOnlySummaryJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            projectName: null,
            file: null,
            severity: "Error",
            diagnosticId: null,
            offset: 0,
            limit: 1,
            summary: true,
            progress: null,
            ct: CancellationToken.None);

        using var unfilteredSummaryDoc = JsonDocument.Parse(unfilteredSummaryJson);
        using var errorOnlySummaryDoc = JsonDocument.Parse(errorOnlySummaryJson);

        AssertTotalDiagnosticsMatchesSumOfSeverityTotals(unfilteredSummaryDoc.RootElement, "severity=null (summary branch)");
        AssertTotalDiagnosticsMatchesSumOfSeverityTotals(errorOnlySummaryDoc.RootElement, "severity=Error (summary branch)");

        Assert.AreEqual(
            unfilteredSummaryDoc.RootElement.GetProperty("totalDiagnostics").GetInt32(),
            errorOnlySummaryDoc.RootElement.GetProperty("totalDiagnostics").GetInt32(),
            "totalDiagnostics must be invariant under severity filter (summary branch).");
    }

    private static void AssertTotalDiagnosticsMatchesSumOfSeverityTotals(JsonElement payload, string label)
    {
        var totalErrors = payload.GetProperty("totalErrors").GetInt32();
        var totalWarnings = payload.GetProperty("totalWarnings").GetInt32();
        var totalInfo = payload.GetProperty("totalInfo").GetInt32();
        var totalDiagnostics = payload.GetProperty("totalDiagnostics").GetInt32();

        Assert.AreEqual(totalErrors + totalWarnings + totalInfo, totalDiagnostics,
            $"totalDiagnostics must equal totalErrors+totalWarnings+totalInfo ({label}).");
    }

    private static void AssertEquivalentResultContract(
        DiagnosticsResultDto expected,
        DiagnosticsResultDto actual,
        string scenario)
    {
        Assert.AreEqual(expected.TotalErrors, actual.TotalErrors, scenario);
        Assert.AreEqual(expected.TotalWarnings, actual.TotalWarnings, scenario);
        Assert.AreEqual(expected.TotalInfo, actual.TotalInfo, scenario);
        Assert.AreEqual(expected.CompilerErrors, actual.CompilerErrors, scenario);
        Assert.AreEqual(expected.AnalyzerErrors, actual.AnalyzerErrors, scenario);
        Assert.AreEqual(expected.WorkspaceErrors, actual.WorkspaceErrors, scenario);
        Assert.AreSequenceEqual(
            expected.WorkspaceDiagnostics,
            actual.WorkspaceDiagnostics,
            scenario);
        Assert.AreSequenceEqual(
            expected.CompilerDiagnostics,
            actual.CompilerDiagnostics,
            scenario);
        Assert.AreSequenceEqual(
            expected.AnalyzerDiagnostics,
            actual.AnalyzerDiagnostics,
            scenario);
    }

    private static void AssertFiltersApplied(
        DiagnosticsResultDto result,
        string? fileFilter,
        string? severityFilter,
        string? diagnosticIdFilter,
        string scenario)
    {
        var returned = result.WorkspaceDiagnostics
            .Concat(result.CompilerDiagnostics)
            .Concat(result.AnalyzerDiagnostics)
            .ToList();
        if (fileFilter is not null)
        {
            Assert.IsTrue(
                returned.All(diagnostic =>
                    diagnostic.FilePath is not null
                    && string.Equals(
                        Path.GetFullPath(diagnostic.FilePath),
                        Path.GetFullPath(fileFilter),
                        StringComparison.OrdinalIgnoreCase)),
                $"The {scenario} query returned a diagnostic outside its file filter.");
        }

        if (severityFilter is not null)
        {
            Assert.IsTrue(
                returned.All(diagnostic => diagnostic.Severity == severityFilter),
                $"The {scenario} query returned a diagnostic below its severity floor.");
        }

        if (diagnosticIdFilter is not null)
        {
            Assert.IsTrue(
                result.CompilerDiagnostics
                    .Concat(result.AnalyzerDiagnostics)
                    .All(diagnostic => diagnostic.Id == diagnosticIdFilter),
                $"The {scenario} query returned a compiler/analyzer diagnostic outside its id filter.");
        }
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
