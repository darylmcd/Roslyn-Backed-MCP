using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct unit tests for <see cref="CompilationCache"/>. Uses an in-memory
/// <see cref="AdhocWorkspace"/> so the cache can be exercised without spinning up a real
/// MSBuildWorkspace, and a fail-closed workspace-manager stub so we can drive the
/// version counter directly to verify invalidation.
/// </summary>
[TestClass]
public sealed class CompilationCacheTests
{
    private const string WorkspaceId = "test-ws";
    private readonly List<IDisposable> _resources = [];

    [TestCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await CleanupFailureCollector.RunAsync(
                "Failed to dispose compilation-cache fixtures.",
                _resources.AsEnumerable().Reverse()
                    .Select(resource => CleanupFailureCollector.FromAction(resource.Dispose)).ToArray());
        }
        finally
        {
            _resources.Clear();
        }
    }

    [TestMethod]
    public async Task ConcurrentFirstCallers_StartOneRawAndOneAnalyzerBuildPerKey()
    {
        var (_, project, ws) = CreateCacheWithProject(initialVersion: 1);
        var rawRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rawStarts = 0;
        var analyzerStarts = 0;
        using var cache = new CompilationCache(
            ws,
            async _ =>
            {
                Interlocked.Increment(ref rawStarts);
                await rawRelease.Task.ConfigureAwait(false);
                return null;
            },
            async (_, _) =>
            {
                Interlocked.Increment(ref analyzerStarts);
                await analyzerRelease.Task.ConfigureAwait(false);
                return null;
            });

        var rawCalls = StartConcurrentBurst(
            () => cache.GetCompilationAsync(WorkspaceId, project, CancellationToken.None));
        rawRelease.SetResult();
        await Task.WhenAll(rawCalls).ConfigureAwait(false);

        var analyzerCalls = StartConcurrentBurst(
            () => cache.GetCompilationWithAnalyzersAsync(WorkspaceId, project, CancellationToken.None));
        analyzerRelease.SetResult();
        await Task.WhenAll(analyzerCalls).ConfigureAwait(false);

        Assert.AreEqual(1, rawStarts, "A first-caller burst must start one raw compilation.");
        Assert.AreEqual(1, analyzerStarts, "A first-caller burst must start one analyzer-bound build.");
    }

    [TestMethod]
    public async Task GetCompilationAsync_ReturnsSameInstance_OnRepeatedCall_AtSameVersion()
    {
        var (cache, project, _) = CreateCacheWithProject(initialVersion: 1);

        var first = await cache.GetCompilationAsync(WorkspaceId, project, default);
        var second = await cache.GetCompilationAsync(WorkspaceId, project, default);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second, "Cache should return the same Compilation instance for the same version.");
    }

    [TestMethod]
    public async Task GetCompilationAsync_PicksUpNewProjectState_AfterVersionBump()
    {
        // Roslyn caches Compilation on the Project itself, so we can't observe invalidation
        // by calling twice on the same Project — Roslyn returns the same instance regardless
        // of our cache. To prove the cache actually re-binds on a version bump we mutate the
        // solution between calls and pass the *new* Project; the cache must store the new
        // entry rather than handing back the stale one.
        const string workspaceId = "test-ws";
        var adhoc = new AdhocWorkspace();
        _resources.Add(adhoc);
        var projectId = ProjectId.CreateNewId();
        adhoc.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(),
            name: "TestProject", assemblyName: "TestProject",
            language: LanguageNames.CSharp));
        var v1Project = adhoc.CurrentSolution.GetProject(projectId)!;

        var version = 1;
        var ws = new FailClosedWorkspaceManagerStub { GetCurrentVersionHandler = _ => version };
        var cache = new CompilationCache(ws);
        _resources.Add(cache);

        var first = await cache.GetCompilationAsync(workspaceId, v1Project, default);
        Assert.IsNotNull(first);
        Assert.AreEqual(0, first!.SyntaxTrees.Count(), "Initial compilation should have no source files.");

        // Mutate the project: add a syntax tree and bump the workspace version.
        var docId = DocumentId.CreateNewId(projectId);
        var newSolution = adhoc.CurrentSolution.AddDocument(
            docId, "Sample.cs", "namespace N { class C { } }");
        var v2Project = newSolution.GetProject(projectId)!;
        version = 2;

        var second = await cache.GetCompilationAsync(workspaceId, v2Project, default);
        Assert.IsNotNull(second);
        Assert.AreEqual(1, second!.SyntaxTrees.Count(),
            "After version bump, cache must compile the new project state, not return the stale v1 entry.");
    }

    [TestMethod]
    public async Task GetCompilationAsync_ReturnsSolutionOwnedCompilation()
    {
        var (cache, project, _) = CreateCacheWithProject(initialVersion: 1);

        var cached = await cache.GetCompilationAsync(WorkspaceId, project, default);
        var owned = await project.GetCompilationAsync(default);

        Assert.AreSame(owned, cached,
            "The plain slot must serve the Solution-owned compilation: symbols from any other " +
            "compilation resolve zero references through SymbolFinder against the Solution.");
    }

    [TestMethod]
    public async Task PlainAndSnapshotSlots_AreCachedIndependently()
    {
        var (_, project, ws) = CreateCacheWithProject(initialVersion: 1);
        var starts = 0;
        using var cache = new CompilationCache(
            ws,
            p =>
            {
                Interlocked.Increment(ref starts);
                return p.GetCompilationAsync(CancellationToken.None);
            },
            analyzerFactory: null);

        var plain = await cache.GetCompilationAsync(WorkspaceId, project, default);
        Assert.AreEqual(1, starts, "The plain slot must populate on its own.");

        var snapshot = await cache.GetCompilationSnapshotAsync(WorkspaceId, project, default);
        Assert.AreEqual(2, starts,
            "The snapshot slot must not reuse the plain slot's entry; it caches its own product.");
        Assert.IsNotNull(snapshot);
        Assert.IsTrue(snapshot!.GeneratorDiagnostics.IsEmpty);

        Assert.AreSame(plain, await cache.GetCompilationAsync(WorkspaceId, project, default));
        Assert.AreSame(snapshot, await cache.GetCompilationSnapshotAsync(WorkspaceId, project, default));
        Assert.AreEqual(2, starts, "Repeat reads at the same version must hit their own slot.");

        cache.Invalidate(WorkspaceId);
        _ = await cache.GetCompilationAsync(WorkspaceId, project, default);
        _ = await cache.GetCompilationSnapshotAsync(WorkspaceId, project, default);
        Assert.AreEqual(4, starts, "Invalidate must clear both the plain and the snapshot slot.");
    }

    [TestMethod]
    public async Task GetCompilationWithAnalyzersAsync_ReturnsNull_WhenProjectHasNoAnalyzers()
    {
        var (cache, project, _) = CreateCacheWithProject(initialVersion: 1);

        var bound = await cache.GetCompilationWithAnalyzersAsync(WorkspaceId, project, default);

        Assert.IsNull(bound, "AdhocWorkspace projects have no AnalyzerReferences, so the cache must return null instead of fabricating an empty bound compilation.");
    }

    [TestMethod]
    public void Invalidate_IsSafeToCall_OnEmptyCache()
    {
        var ws = new FailClosedWorkspaceManagerStub { GetCurrentVersionHandler = _ => 1 };
        var cache = new CompilationCache(ws);
        _resources.Add(cache);

        // Smoke test — Invalidate should be a no-op when nothing is cached for the workspace.
        // (Roslyn's Project-level Compilation cache makes a stronger "evicted-and-rebuilt"
        // assertion impossible without reflection, but the method must not throw.)
        cache.Invalidate("test-ws");
        cache.Invalidate("nonexistent-ws");
    }

    private (CompilationCache cache, Project project, FailClosedWorkspaceManagerStub ws) CreateCacheWithProject(int initialVersion)
    {
        var adhoc = new AdhocWorkspace();
        _resources.Add(adhoc);
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp);
        adhoc.AddProject(projectInfo);
        var project = adhoc.CurrentSolution.GetProject(projectId)!;

        var ws = new FailClosedWorkspaceManagerStub { GetCurrentVersionHandler = _ => initialVersion };
        var cache = new CompilationCache(ws);
        _resources.Add(cache);
        return (cache, project, ws);
    }

    private static Task<T>[] StartConcurrentBurst<T>(Func<Task<T>> operation)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callers = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                return await operation().ConfigureAwait(false);
            }))
            .ToArray();

        start.SetResult();
        return callers;
    }
}
