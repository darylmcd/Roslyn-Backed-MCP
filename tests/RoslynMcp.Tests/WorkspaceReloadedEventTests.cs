using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #7 — regression guard for `compile-check-stale-assembly-refs-post-reload`.
/// The `WorkspaceReloaded` event is the contract used by per-workspace caches
/// (CompilationCache, DiagnosticService, DiRegistrationService, NuGetDependencyService)
/// to drop stale entries synchronously with a reload. These tests enforce the event
/// contract — they do not attempt to reproduce the Roslyn-internal
/// MetadataReference-caching story that motivated the fix (that requires a cross-
/// restore package upgrade), but they keep the invalidation plumbing honest so the
/// symptom class doesn't regress.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceReloadedEventTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Reload_Fires_WorkspaceReloaded_Event_With_Current_WorkspaceId()
    {
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        var observed = new List<string>();
        void Handler(string id) => observed.Add(id);

        WorkspaceManager.WorkspaceReloaded += Handler;
        try
        {
            await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);
        }
        finally
        {
            WorkspaceManager.WorkspaceReloaded -= Handler;
        }

        Assert.AreEqual(1, observed.Count, "WorkspaceReloaded must fire exactly once per successful reload.");
        Assert.AreEqual(workspaceId, observed[0], "Reload event must carry the reloaded workspace's identifier.");
    }

    [TestMethod]
    public async Task Reload_Handler_Exception_Does_Not_Break_Reload_Path()
    {
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        var observed = false;
        void ThrowingHandler(string _) => throw new InvalidOperationException("deliberately thrown in test");
        void ObservingHandler(string id) => observed = id == workspaceId;
        WorkspaceManager.WorkspaceReloaded += ThrowingHandler;
        WorkspaceManager.WorkspaceReloaded += ObservingHandler;
        try
        {
            var status = await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);
            Assert.IsNotNull(status, "Reload must still return a valid status when a handler throws.");
        }
        finally
        {
            WorkspaceManager.WorkspaceReloaded -= ThrowingHandler;
            WorkspaceManager.WorkspaceReloaded -= ObservingHandler;
        }

        Assert.IsTrue(
            observed,
            "A throwing subscriber must not suppress later WorkspaceReloaded subscribers.");
    }

    [TestMethod]
    public async Task Close_Handler_Exception_Does_Not_Suppress_Later_Subscribers()
    {
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        var observed = false;
        void ThrowingHandler(string _) => throw new InvalidOperationException("deliberately thrown in test");
        void ObservingHandler(string id) => observed = id == workspaceId;
        WorkspaceManager.WorkspaceClosed += ThrowingHandler;
        WorkspaceManager.WorkspaceClosed += ObservingHandler;
        try
        {
            Assert.IsTrue(WorkspaceManager.Close(workspaceId));
        }
        finally
        {
            WorkspaceManager.WorkspaceClosed -= ThrowingHandler;
            WorkspaceManager.WorkspaceClosed -= ObservingHandler;
        }

        Assert.IsTrue(
            observed,
            "A throwing subscriber must not suppress later WorkspaceClosed subscribers.");

        var reloadedWorkspaceId = await GetOrLoadWorkspaceIdAsync(
            SampleSolutionPath,
            CancellationToken.None);
        Assert.AreNotEqual(
            workspaceId,
            reloadedWorkspaceId,
            "The shared fixture cache must replace an evicted workspace id after close.");
    }

    [TestMethod]
    [DataRow("reload")]
    [DataRow("close")]
    public async Task LifecycleHandlerFailures_LogOnlySanitizedMetadata_AndContinueDispatch(string lifecycle)
    {
        const string secretSentinel = "C:/private/workspace/subscriber-secret.txt";
        var solutionPath = CreateSampleSolutionCopy();
        var logger = new ListLogger<WorkspaceManager>();
        using var watcher = new FileWatcherService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileWatcherService>.Instance);
        using var manager = new WorkspaceManager(
            logger,
            new PreviewStore(),
            watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 1 });

        try
        {
            var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
            var observed = false;
            void ThrowingHandler(string _) => throw new InvalidOperationException(secretSentinel);
            void ObservingHandler(string id) => observed = id == status.WorkspaceId;

            if (lifecycle == "reload")
            {
                manager.WorkspaceReloaded += ThrowingHandler;
                manager.WorkspaceReloaded += ObservingHandler;
                await manager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
            }
            else
            {
                manager.WorkspaceClosed += ThrowingHandler;
                manager.WorkspaceClosed += ObservingHandler;
                Assert.IsTrue(manager.Close(status.WorkspaceId));
            }

            Assert.IsTrue(observed, $"A failing {lifecycle} subscriber must not suppress later subscribers.");
            var warnings = logger.Entries
                .Where(entry => entry.Level == LogLevel.Warning &&
                                entry.Message.Contains("handler threw", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(1, warnings.Length, $"{lifecycle} should emit one sanitized warning.");
            foreach (var warning in warnings)
            {
                Assert.IsNull(warning.Exception, "Raw subscriber exceptions must not be attached to logs.");
                StringAssert.Contains(warning.Message, typeof(InvalidOperationException).FullName!);
                Assert.IsFalse(warning.Message.Contains(secretSentinel, StringComparison.Ordinal), warning.Message);
                StringAssert.Contains(warning.Message, status.WorkspaceId);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(solutionPath)!);
        }
    }

    [TestMethod]
    public async Task Reload_Bumps_Workspace_Version()
    {
        // Version bump is the legacy invalidation signal; the Item #7 event augments it rather
        // than replacing it. Keep the version-bump contract tested so future cache authors can
        // choose either signal with confidence.
        var workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        var before = WorkspaceManager.GetCurrentVersion(workspaceId);
        await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);
        var after = WorkspaceManager.GetCurrentVersion(workspaceId);

        Assert.IsTrue(after > before, $"Reload must bump version. Before={before}, After={after}");
    }
}
