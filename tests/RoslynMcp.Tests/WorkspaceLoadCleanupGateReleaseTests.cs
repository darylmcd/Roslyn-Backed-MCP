using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>workspace-native-load-cleanup-gate-release</c>: a throwing disposal of a retired
/// (swap) or half-loaded (failure) workspace/analyzer lease inside the load finally must never leave the
/// session's <c>LoadLock</c> held, mask the primary load failure or cancellation, or fail a committed reload.
/// </summary>
// Same isolation rationale as WorkspaceLoadRegistrationCompensationTests: private manager per test, no shared
// state beyond the checked-in samples/SampleSolution obj/ artifacts.
[TestClass]
public sealed class WorkspaceLoadCleanupGateReleaseTests
{
    private static string _sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        _sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            repoRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    private static WorkspaceManager CreateManager(
        ILogger<WorkspaceManager> logger,
        WorkspaceSessionLoader loader,
        UnresolvedAnalyzerReferenceStripper? stripper = null) =>
        new(
            logger,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 },
            cacheStore: null,
            sessionLoader: loader,
            analyzerReferenceStripper: stripper,
            ownsFileWatcher: true);

    private static CancellationToken Bounded(int seconds = 120) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    [TestMethod]
    public async Task Reload_RetiredLeaseDisposeThrows_ReloadCommits_AndLoadLockIsReleased()
    {
        var logger = new ListLogger<WorkspaceManager>();
        // Call 1 (initial load) hands the session a lease whose Dispose throws; the reload swap retires it.
        var loader = new ScriptedLoader(throwingLeaseCalls: [1]);
        using var manager = CreateManager(logger, loader);
        var status = await manager.LoadAsync(_sampleSolutionPath, Bounded());
        var versionBefore = manager.GetStatus(status.WorkspaceId).WorkspaceVersion;

        var reloaded = await manager.ReloadAsync(status.WorkspaceId, Bounded());

        Assert.IsTrue(reloaded.IsLoaded, "A committed reload must not be reported as failed because retired-resource cleanup threw.");
        Assert.AreEqual(versionBefore + 1, reloaded.WorkspaceVersion);
        Assert.IsNotNull(manager.GetCurrentSolution(status.WorkspaceId));
        var log = logger.Entries.Single(e => e.Level == LogLevel.Error && e.Exception is AggregateException);
        StringAssert.Contains(log.Message, "retired");
        Assert.IsInstanceOfType<NullReferenceException>(((AggregateException)log.Exception!).InnerExceptions.Single());

        // A second reload must acquire LoadLock: on unfixed code it is still held and this blocks until the token fires.
        var again = await manager.ReloadAsync(status.WorkspaceId, Bounded(60));
        Assert.AreEqual(versionBefore + 2, again.WorkspaceVersion);
    }

    [TestMethod]
    public async Task Reload_PostLoaderFailure_NewLeaseDisposeThrows_PreservesPrimaryFailure_AndReleasesLock()
    {
        var logger = new ListLogger<WorkspaceManager>();
        var loader = new ScriptedLoader(throwingLeaseCalls: [2]);
        var stripper = new ScriptedStripper { ThrowOnCall = 2, Failure = () => new InvalidOperationException("primary failure") };
        using var manager = CreateManager(logger, loader, stripper);
        var status = await manager.LoadAsync(_sampleSolutionPath, Bounded());
        var versionBefore = manager.GetStatus(status.WorkspaceId).WorkspaceVersion;

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.ReloadAsync(status.WorkspaceId, Bounded()));

        Assert.AreEqual("primary failure", thrown.Message, "A cleanup failure must not replace the primary load failure.");
        Assert.AreEqual(versionBefore, manager.GetStatus(status.WorkspaceId).WorkspaceVersion, "Failed reload must not swap.");
        Assert.IsNotNull(manager.GetCurrentSolution(status.WorkspaceId), "Prior workspace must stay readable.");
        var log = logger.Entries.Single(e => e.Level == LogLevel.Error && e.Exception is AggregateException);
        StringAssert.Contains(log.Message, "half-loaded");

        var after = await manager.ReloadAsync(status.WorkspaceId, Bounded(60));
        Assert.AreEqual(versionBefore + 1, after.WorkspaceVersion, "LoadLock must be released after the failed reload.");
    }

    [TestMethod]
    public async Task Reload_PostLoaderCancellation_NewLeaseDisposeThrows_PreservesOperationCanceled_AndReleasesLock()
    {
        var logger = new ListLogger<WorkspaceManager>();
        var loader = new ScriptedLoader(throwingLeaseCalls: [2]);
        var stripper = new ScriptedStripper { ThrowOnCall = 2, Failure = () => new OperationCanceledException("cancelled") };
        using var manager = CreateManager(logger, loader, stripper);
        var status = await manager.LoadAsync(_sampleSolutionPath, Bounded());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => manager.ReloadAsync(status.WorkspaceId, Bounded()));

        var after = await manager.ReloadAsync(status.WorkspaceId, Bounded(60));
        Assert.IsTrue(after.IsLoaded, "LoadLock must be released after the cancelled reload.");
    }

    [TestMethod]
    public async Task Reload_RetiredResourceCleanupHealthy_LogsNothingAtError()
    {
        var logger = new ListLogger<WorkspaceManager>();
        using var manager = CreateManager(logger, new ScriptedLoader(throwingLeaseCalls: []));
        var status = await manager.LoadAsync(_sampleSolutionPath, Bounded());

        await manager.ReloadAsync(status.WorkspaceId, Bounded());

        Assert.IsFalse(logger.Entries.Any(e => e.Level == LogLevel.Error), "No cleanup failure means no error log.");
    }

    /// <summary>
    /// Real loads; on the listed 1-based calls the real lease is disposed and replaced by one whose Dispose throws
    /// (null loader element: NullReferenceException is deterministic; the real loader type is sealed internal).
    /// </summary>
    private sealed class ScriptedLoader(int[] throwingLeaseCalls) : WorkspaceSessionLoader
    {
        private int _calls;

        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
            string workspaceId,
            string path,
            IDictionary<string, string>? globalProperties,
            WorkspaceDiagnosticsSink diagnostics,
            ILogger logger,
            CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            var (workspace, lease) = await base.CreateAndOpenAsync(
                workspaceId, path, globalProperties, diagnostics, logger, ct).ConfigureAwait(false);
            if (!throwingLeaseCalls.Contains(call)) return (workspace, lease);

            lease.Dispose();
            return (workspace, new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                null, new AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader[] { null! }, 0, null));
        }
    }

    private sealed class ScriptedStripper : UnresolvedAnalyzerReferenceStripper
    {
        private int _calls;

        public int ThrowOnCall { get; set; }
        public Func<Exception> Failure { get; set; } = () => new InvalidOperationException();

        public override Task StripAsync(
            string workspaceId,
            MSBuildWorkspace workspace,
            ConcurrentQueue<DiagnosticDto> diagnosticsQueue,
            int maxDiagnosticsPerWorkspace,
            ILogger logger,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == ThrowOnCall) throw Failure();
            return base.StripAsync(workspaceId, workspace, diagnosticsQueue, maxDiagnosticsPerWorkspace, logger, ct);
        }
    }
}
