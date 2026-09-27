using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for the <c>workspace-manager-loadintosession-split</c> refactor: when the
/// <see cref="WorkspaceSessionLoader"/> throws partway through a reload, the
/// <see cref="WorkspaceManager"/> session must retain its prior loaded workspace
/// (non-disposed, observable via <see cref="WorkspaceManager.GetCurrentSolution"/>) and the
/// auto-reload-cascade invariant from <c>autoreload-cascade-stdio-host-crash</c> must hold.
///
/// <para>
/// The test injects a loader double that delegates to the real loader on the first call (the
/// initial <see cref="WorkspaceManager.LoadAsync"/>) and throws on every subsequent call
/// (the <see cref="WorkspaceManager.ReloadAsync"/>). After the throwing reload, readers must
/// still see the original workspace's <see cref="Microsoft.CodeAnalysis.Solution"/>.
/// </para>
/// </summary>
// donotparallelize-audit-wave-39: [DoNotParallelize] removed. The single test builds a private WorkspaceManager
// (own PreviewStore, FileWatcherService, options and the nested ThrowAfterFirstCallLoader double) and never touches
// the TestBase services or WorkspaceIdCache. The loader's CallCount is per-instance, WorkspaceManager holds no
// mutable statics, and the lookup path never reaches the static WorkspaceEvictionRegistry because the session stays
// loaded. Its one shared resource is the checked-in samples/SampleSolution: the load's MSBuild design-time build
// writes obj/ artifacts there, as does the TestBase manager's cached load that parallel readers trigger. Verified
// with repeated warm (5x) and cold (3x, samples obj/bin cleaned first) runs alongside its wave-39 siblings and the
// parallel SampleSolution readers IntegrationTests_WorkspaceCore/IntegrationTests_SymbolNavigation.
[TestClass]
public sealed class WorkspaceSessionLoaderFailureTests
{
    private static string s_sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        s_sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            repoRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    [TestMethod]
    public async Task LoaderThrowsOnReload_SessionPreservesOldWorkspace_AndCurrentSolutionStillReadable()
    {
        var loader = new ThrowAfterFirstCallLoader();
        using var manager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
            cacheStore: null,
            sessionLoader: loader);

        var status = await manager.LoadAsync(s_sampleSolutionPath, CancellationToken.None);
        Assert.AreEqual(1, loader.CallCount, "Initial LoadAsync must invoke the loader exactly once.");
        Assert.IsTrue(status.IsLoaded, "Initial load must succeed before exercising the throwing reload path.");

        var solutionBeforeReload = manager.GetCurrentSolution(status.WorkspaceId);
        Assert.IsNotNull(solutionBeforeReload, "Solution must be observable after initial load.");
        var projectCountBeforeReload = solutionBeforeReload.Projects.Count();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.ReloadAsync(status.WorkspaceId, CancellationToken.None));
        Assert.AreEqual(2, loader.CallCount, "ReloadAsync must invoke the loader once more (the throwing call).");

        var solutionAfterFailedReload = manager.GetCurrentSolution(status.WorkspaceId);
        Assert.IsNotNull(solutionAfterFailedReload, "Solution must still be observable after the loader threw.");
        Assert.AreEqual(projectCountBeforeReload, solutionAfterFailedReload.Projects.Count(),
            "Project graph must be the prior loaded snapshot (loader threw before any swap).");
    }

    private sealed class ThrowAfterFirstCallLoader : WorkspaceSessionLoader
    {
        public int CallCount { get; private set; }

        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
            string workspaceId,
            string path,
            IDictionary<string, string>? globalProperties,
            WorkspaceDiagnosticsSink diagnostics,
            ILogger logger,
            CancellationToken ct)
        {
            CallCount++;
            if (CallCount > 1)
            {
                throw new InvalidOperationException(
                    "Simulated loader failure on call #" + CallCount + " (workspace=" + workspaceId + ").");
            }
            return await base.CreateAndOpenAsync(
                workspaceId, path, globalProperties, diagnostics, logger, ct).ConfigureAwait(false);
        }
    }
}
