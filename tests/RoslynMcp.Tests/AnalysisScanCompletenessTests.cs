using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalysisScanCompletenessTests : IsolatedWorkspaceTestBase
{
    private const string _secretMarker = "secret-scan-detail";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("reflection")]
    [DataRow("di")]
    [DataRow("exception-flow")]
    public async Task DetailedSyntaxTreeScan_MixedFailure_RetainsHealthyDataAndReportsSafeCompleteness(
        string scanKind)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        await WriteScanFixturesAsync(workspace);

        var reporter = new RecordingUnexpectedExceptionReporter();
        var compilationCache = new CompilationCache(WorkspaceManager);
        var probe = await RunDetailedSyntaxTreeScanAsync(
            scanKind,
            workspace.WorkspaceId,
            compilationCache,
            reporter,
            static (tree, token) =>
            {
                if (string.Equals(Path.GetFileName(tree.FilePath), "ScanFailure.cs", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(_secretMarker);
                }

                return tree.GetRootAsync(token);
            },
            CancellationToken.None);

        Assert.IsFalse(probe.IsComplete);
        Assert.AreEqual(1, probe.FailedCount);
        Assert.IsTrue(probe.ObservedCount > 0, "Healthy documents must still contribute usable results.");
        Assert.AreEqual(1, reporter.ReportCount);
        Assert.AreEqual(UnexpectedExceptionCategory.AnalysisScan, reporter.Categories.Single());
        StringAssert.Contains(probe.LogMessage, "correlationId=scan-test-1");
        Assert.IsFalse(probe.LogMessage.Contains(_secretMarker, StringComparison.Ordinal));
        Assert.IsNull(probe.LoggedException, "Raw exception objects must not be attached to safe scan diagnostics.");
    }

    [TestMethod]
    [DataRow("reflection")]
    [DataRow("di")]
    [DataRow("exception-flow")]
    public async Task DetailedSyntaxTreeScan_Cancelled_PropagatesCancellation(string scanKind)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var reporter = new RecordingUnexpectedExceptionReporter();
        var compilationCache = new CompilationCache(WorkspaceManager);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RunDetailedSyntaxTreeScanAsync(
                scanKind,
                workspace.WorkspaceId,
                compilationCache,
                reporter,
                static (tree, token) => tree.GetRootAsync(token),
                cancellation.Token));

        Assert.AreEqual(0, reporter.ReportCount, "Cancellation is control flow, not an unexpected failure.");
    }

    [TestMethod]
    public async Task NuGetDetailedScan_MixedFailure_RetainsHealthyProjectsAndCompatibilityProjectionFailsClosed()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var reporter = new RecordingUnexpectedExceptionReporter();
        var logger = new CaptureLogger<NuGetDependencyService>();
        var evaluator = new MixedOutcomeMsBuildEvaluationService("SampleLib");
        var service = new NuGetDependencyService(
            WorkspaceManager,
            GatedCommandExecutor,
            evaluator,
            logger,
            exceptionReporter: reporter);

        var scan = await service.GetNuGetDependenciesDetailedAsync(
            workspace.WorkspaceId,
            CancellationToken.None,
            summary: false);

        Assert.IsFalse(scan.IsComplete);
        Assert.AreEqual(1, scan.FailedProjectCount);
        Assert.IsTrue(scan.Result.Projects.Count > 0, "Healthy projects must remain in the partial graph.");
        Assert.IsFalse(scan.Result.Projects.Any(project =>
            string.Equals(project.ProjectName, "SampleLib", StringComparison.Ordinal)));
        Assert.AreEqual(1, reporter.ReportCount);
        Assert.AreEqual(UnexpectedExceptionCategory.AnalysisScan, reporter.Categories.Single());

        var log = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        StringAssert.Contains(log.Message, "correlationId=scan-test-1");
        Assert.IsFalse(log.Message.Contains(_secretMarker, StringComparison.Ordinal));
        Assert.IsNull(log.Exception);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.GetNuGetDependenciesAsync(
                workspace.WorkspaceId,
                CancellationToken.None,
                summary: false));
    }

    [TestMethod]
    public async Task NuGetDetailedScan_Cancelled_PropagatesCancellation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var reporter = new RecordingUnexpectedExceptionReporter();
        var service = new NuGetDependencyService(
            WorkspaceManager,
            GatedCommandExecutor,
            new MixedOutcomeMsBuildEvaluationService("SampleLib"),
            new CaptureLogger<NuGetDependencyService>(),
            exceptionReporter: reporter);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.GetNuGetDependenciesDetailedAsync(
                workspace.WorkspaceId,
                cancellation.Token,
                summary: false));
        Assert.AreEqual(0, reporter.ReportCount);
    }

    private static async Task<ScanProbe> RunDetailedSyntaxTreeScanAsync(
        string scanKind,
        string workspaceId,
        ICompilationCache compilationCache,
        IUnexpectedExceptionReporter reporter,
        Func<SyntaxTree, CancellationToken, Task<SyntaxNode>> rootLoader,
        CancellationToken cancellationToken)
    {
        switch (scanKind)
        {
            case "reflection":
                {
                    var logger = new CaptureLogger<CodePatternAnalyzer>();
                    var service = new CodePatternAnalyzer(
                        WorkspaceManager, compilationCache, logger, reporter, rootLoader);
                    var result = await service.FindReflectionUsagesDetailedAsync(
                        workspaceId, "SampleLib", cancellationToken);
                    return CreateProbe(result.IsComplete, result.FailedDocumentCount, result.Usages.Count, logger);
                }
            case "di":
                {
                    var logger = new CaptureLogger<DiRegistrationService>();
                    var service = new DiRegistrationService(
                        WorkspaceManager, compilationCache, logger, reporter, rootLoader);
                    var result = await service.GetDiRegistrationsDetailedAsync(
                        workspaceId, "SampleLib", includeOverrideChains: false, cancellationToken);
                    return CreateProbe(result.IsComplete, result.FailedDocumentCount, result.Registrations.Count, logger);
                }
            case "exception-flow":
                {
                    var logger = new CaptureLogger<ExceptionFlowService>();
                    var service = new ExceptionFlowService(
                        WorkspaceManager, compilationCache, logger, reporter, rootLoader);
                    var result = await service.TraceExceptionFlowAsync(
                        workspaceId,
                        "System.InvalidOperationException",
                        "SampleLib",
                        maxResults: null,
                        cancellationToken);
                    return CreateProbe(
                        result.IsComplete,
                        result.FailedDocumentCount,
                        result.CatchSites.Count + result.ThrowSites.Count,
                        logger);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(scanKind), scanKind, "Unknown scan kind.");
        }
    }

    private static ScanProbe CreateProbe<T>(
        bool isComplete,
        int failedCount,
        int observedCount,
        CaptureLogger<T> logger)
    {
        var entry = logger.Entries.Single(item => item.Level == LogLevel.Warning);
        return new ScanProbe(
            isComplete,
            failedCount,
            observedCount,
            entry.Message,
            entry.Exception);
    }

    private static async Task WriteScanFixturesAsync(IsolatedWorkspaceScope workspace)
    {
        var healthyPath = workspace.GetPath(Path.Combine("SampleLib", "ScanHealthy.cs"));
        var failurePath = workspace.GetPath(Path.Combine("SampleLib", "ScanFailure.cs"));
        await File.WriteAllTextAsync(healthyPath, """
            using System;

            namespace SampleLib;

            public interface IServiceCollection { }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }

            public static class ScanHealthy
            {
                public static Type Reflection() => typeof(string);
                public static void Register(IServiceCollection services) => services.AddSingleton<object>();
                public static void Handle()
                {
                    try { throw new InvalidOperationException(); }
                    catch (InvalidOperationException) { }
                }
            }
            """, CancellationToken.None);
        await File.WriteAllTextAsync(failurePath, "namespace SampleLib; public static class ScanFailure { }", CancellationToken.None);
        await workspace.ReloadAsync(CancellationToken.None);
    }

    private sealed record ScanProbe(
        bool IsComplete,
        int FailedCount,
        int ObservedCount,
        string LogMessage,
        Exception? LoggedException);

    private sealed class RecordingUnexpectedExceptionReporter : IUnexpectedExceptionReporter
    {
        public int ReportCount { get; private set; }

        public List<UnexpectedExceptionCategory> Categories { get; } = [];

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            ReportCount++;
            Categories.Add(category);
            return PublicExceptionDetailPolicy.ProjectUnexpected(exception, $"scan-test-{ReportCount}");
        }
    }

    private sealed class MixedOutcomeMsBuildEvaluationService(string failingProject) : IMsBuildEvaluationService
    {
        public Task<MsBuildItemEvaluationDto> EvaluateItemsAsync(
            string workspaceId,
            string projectName,
            string itemType,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.Equals(projectName, failingProject, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(_secretMarker);
            }

            return Task.FromResult(new MsBuildItemEvaluationDto(projectName, projectName, itemType, []));
        }

        public Task<MsBuildPropertyEvaluationDto> EvaluatePropertyAsync(
            string workspaceId,
            string projectName,
            string propertyName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<MsBuildPropertiesDumpDto> GetEvaluatedPropertiesAsync(
            string workspaceId,
            string projectName,
            string? propertyNameFilter,
            IReadOnlyCollection<string>? includedNames,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
