using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>compilation-cache-adoption-read-side</c>: the read-side analysis
/// services must obtain project compilations through the shared <see cref="ICompilationCache"/>
/// rather than calling <c>project.GetCompilationAsync</c> directly. Batch 1 covered
/// <see cref="CouplingAnalysisService"/>, <see cref="ExceptionFlowService"/>, and
/// <see cref="AnalyzerInfoService"/>; batch 2 adds <see cref="TypeConsumersService"/>,
/// <see cref="CodePatternAnalyzer"/>, and <see cref="SymbolSearchService"/>.
/// <para>
/// A reference-equality check alone cannot prove adoption — Roslyn memoizes a
/// <see cref="Compilation"/> on its owning <see cref="Project"/>, so two direct calls would also
/// return the same instance. The recording cache below makes the routing observable: if a service
/// bypassed the cache, <see cref="RecordingCompilationCache.GetCompilationCallCount"/> would stay
/// at zero. The second assertion confirms the cache hands back the reference-equal warm
/// compilation across successive read calls at an unchanged workspace version.
/// </para>
/// </summary>
[TestClass]
public sealed class CompilationCacheAdoptionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task CouplingAnalysisService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new CouplingAnalysisService(WorkspaceManager, cache, NullLogger<CouplingAnalysisService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.GetCouplingMetricsAsync(
                workspace.WorkspaceId,
                projectFilter: null,
                limit: 50,
                excludeTestProjects: false,
                includeInterfaces: false,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task ExceptionFlowService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new ExceptionFlowService(WorkspaceManager, cache, NullLogger<ExceptionFlowService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.TraceExceptionFlowAsync(
                workspace.WorkspaceId,
                "System.Exception",
                scopeProjectFilter: null,
                maxResults: null,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task AnalyzerInfoService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new AnalyzerInfoService(WorkspaceManager, cache, NullLogger<AnalyzerInfoService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.ListAnalyzersAsync(workspace.WorkspaceId, projectFilter: null, CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task TypeConsumersService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new TypeConsumersService(WorkspaceManager, cache, NullLogger<TypeConsumersService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.FindTypeConsumersAsync(
                workspace.WorkspaceId,
                "SampleLib.IAnimal",
                limit: 100,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task CodePatternAnalyzer_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new CodePatternAnalyzer(WorkspaceManager, cache, NullLogger<CodePatternAnalyzer>.Instance);

        // Exercises BOTH converted call paths in one method: FindReflectionUsagesAsync (the
        // instance-method site) and SemanticSearchAsync -> CollectSemanticSearchMatchesAsync
        // (the private static helper site).
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, async () =>
        {
            await service.FindReflectionUsagesAsync(workspace.WorkspaceId, projectFilter: null, CancellationToken.None);
            await service.SemanticSearchAsync(workspace.WorkspaceId, "Dog", projectFilter: null, limit: 50, CancellationToken.None);
        });

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task SymbolSearchService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new SymbolSearchService(WorkspaceManager, cache, NullLogger<SymbolSearchService>.Instance);

        // maxResults is deliberately high so the primary pattern search leaves budget under the
        // cap and the FQN-substring fallback (the converted GetCompilationAsync site) runs.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.SearchSymbolsAsync(
                workspace.WorkspaceId,
                "IAnimal",
                projectFilter: null,
                kindFilter: null,
                namespaceFilter: null,
                maxResults: 100,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    /// <summary>
    /// Runs the service once (proving it touched the cache), snapshots the compilations the cache
    /// handed out, then runs it a second time at the unchanged workspace version so the caller can
    /// assert the warm compilations were reused.
    /// </summary>
    private static async Task<IReadOnlyDictionary<ProjectId, Compilation>> RunTwiceAndCaptureAsync(
        RecordingCompilationCache cache, Func<Task> invokeService)
    {
        await invokeService();
        Assert.IsTrue(cache.GetCompilationCallCount > 0,
            "Service must obtain its compilation through ICompilationCache, not project.GetCompilationAsync.");
        var afterFirstRun = cache.SnapshotReturned();

        await invokeService();
        return afterFirstRun;
    }

    private static void AssertRoutedThroughCacheAndShared(
        RecordingCompilationCache cache, IReadOnlyDictionary<ProjectId, Compilation> afterFirstRun)
    {
        Assert.IsTrue(afterFirstRun.Count > 0, "Expected at least one project compilation to flow through the cache.");

        var afterSecondRun = cache.SnapshotReturned();
        foreach (var (projectId, firstCompilation) in afterFirstRun)
        {
            Assert.IsTrue(afterSecondRun.TryGetValue(projectId, out var secondCompilation),
                "The same project should be compiled through the cache on the repeat read.");
            Assert.AreSame(firstCompilation, secondCompilation,
                "At an unchanged workspace version the cache must hand back the reference-equal warm compilation.");
        }
    }

    /// <summary>
    /// <see cref="ICompilationCache"/> decorator that records how often <c>GetCompilationAsync</c>
    /// was invoked and which <see cref="Compilation"/> instance was returned per project, while
    /// delegating to a real <see cref="CompilationCache"/> so the version-keyed sharing contract
    /// is exercised end to end.
    /// </summary>
    private sealed class RecordingCompilationCache : ICompilationCache
    {
        private readonly ICompilationCache _inner;
        private readonly ConcurrentDictionary<ProjectId, Compilation> _returned = new();
        private int _getCompilationCallCount;

        public RecordingCompilationCache(ICompilationCache inner) => _inner = inner;

        public int GetCompilationCallCount => Volatile.Read(ref _getCompilationCallCount);

        public IReadOnlyDictionary<ProjectId, Compilation> SnapshotReturned() =>
            new Dictionary<ProjectId, Compilation>(_returned);

        public async Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct)
        {
            Interlocked.Increment(ref _getCompilationCallCount);
            var compilation = await _inner.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is not null)
            {
                _returned[project.Id] = compilation;
            }

            return compilation;
        }

        public Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct) =>
            _inner.GetCompilationWithAnalyzersAsync(workspaceId, project, ct);

        public void Invalidate(string workspaceId) => _inner.Invalidate(workspaceId);
    }
}
