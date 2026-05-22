using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Services;

[TestClass]
public sealed class WorkspaceCacheCoordinatorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "RoslynMcpTests",
            "WorkspaceCacheCoordinator",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; temp files are safe to leak until OS cleanup.
        }
    }

    [TestMethod]
    public async Task TryProbeAsync_EmptyStore_ReturnsMissProbe()
    {
        var solutionPath = WriteSolutionFile("empty.slnx", "<Solution></Solution>");
        var coordinator = CreateCoordinator(new WorkspaceCacheStore(_root));

        var probe = await coordinator.TryProbeAsync(solutionPath, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.IsNull(probe.CachedEntry);
        Assert.IsFalse(probe.MetadataReferencesStillStable);
        Assert.AreEqual(
            await WorkspaceCacheCoordinator.ComputeSolutionContentHashAsync(solutionPath, CancellationToken.None),
            probe.SolutionHash);
        Assert.AreEqual(WorkspaceCacheCoordinator.ResolveSdkVersionForCacheKey(), probe.SdkVersion);
    }

    [TestMethod]
    public async Task TryProbeAsync_StaleMetadataReference_ReturnsProbeWithStableFalse()
    {
        var solutionPath = WriteSolutionFile("stale.slnx", "<Solution><Project Path=\"A.csproj\" /></Solution>");
        var referencePath = Path.Combine(_root, "ref.dll");
        File.WriteAllText(referencePath, "v1");
        var capturedMtime = File.GetLastWriteTimeUtc(referencePath);

        var store = new WorkspaceCacheStore(_root);
        var solutionHash = await WorkspaceCacheCoordinator.ComputeSolutionContentHashAsync(solutionPath, CancellationToken.None);
        Assert.IsNotNull(solutionHash);
        var sdkVersion = WorkspaceCacheCoordinator.ResolveSdkVersionForCacheKey();
        var graph = new[]
        {
            new CachedProjectGraphNode(Path.Combine(_root, "A.csproj"), Array.Empty<string>()),
        };
        var graphHash = WorkspaceCacheCoordinator.ComputeMsbuildGraphHash(graph);
        var entry = new WorkspaceCacheEntry(
            graph,
            new[]
            {
                new CachedProjectMetadataReferences(
                    Path.Combine(_root, "A.csproj"),
                    new[]
                    {
                        new CachedMetadataReference(referencePath, capturedMtime),
                    }),
            },
            DateTime.UtcNow);
        Assert.IsTrue(await store.PutAsync(new WorkspaceCacheKey(solutionHash, sdkVersion, graphHash), entry, CancellationToken.None));
        File.SetLastWriteTimeUtc(referencePath, capturedMtime.AddMinutes(1));

        var probe = await CreateCoordinator(store).TryProbeAsync(solutionPath, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.IsNotNull(probe.CachedEntry);
        Assert.IsFalse(probe.MetadataReferencesStillStable);
    }

    [TestMethod]
    public async Task ResolveAndWriteAsync_MatchingGraphAndStableMetadata_ReturnsCacheHitTrue()
    {
        var solutionPath = WriteSolutionFile("matching.slnx", "<Solution><Project Path=\"App.csproj\" /></Solution>");
        var projectPath = Path.Combine(_root, "App.csproj");
        var solution = CreateSolution(projectPath);
        var graph = WorkspaceCacheCoordinator.BuildCachedGraphFromSolution(solution);
        var graphHash = WorkspaceCacheCoordinator.ComputeMsbuildGraphHash(graph);
        var solutionHash = await WorkspaceCacheCoordinator.ComputeSolutionContentHashAsync(solutionPath, CancellationToken.None);
        Assert.IsNotNull(solutionHash);
        var sdkVersion = WorkspaceCacheCoordinator.ResolveSdkVersionForCacheKey();
        var cachedEntry = new WorkspaceCacheEntry(graph, Array.Empty<CachedProjectMetadataReferences>(), DateTime.UtcNow.AddMinutes(-5));
        var store = new WorkspaceCacheStore(_root);
        var coordinator = CreateCoordinator(store);
        var probe = new WorkspaceCacheProbe(solutionHash, sdkVersion, cachedEntry, MetadataReferencesStillStable: true);

        var result = await coordinator.ResolveAndWriteAsync(solution, probe, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.CacheHit);
        var written = await store.TryGetAsync(new WorkspaceCacheKey(solutionHash, sdkVersion, graphHash), CancellationToken.None);
        Assert.IsNotNull(written);
    }

    [TestMethod]
    public async Task ResolveAndWriteAsync_GraphMismatch_ReturnsCacheHitFalseAndWritesActualGraph()
    {
        var solutionPath = WriteSolutionFile("mismatch.slnx", "<Solution><Project Path=\"App.csproj\" /></Solution>");
        var projectPath = Path.Combine(_root, "App.csproj");
        var solution = CreateSolution(projectPath);
        var actualGraph = WorkspaceCacheCoordinator.BuildCachedGraphFromSolution(solution);
        var actualGraphHash = WorkspaceCacheCoordinator.ComputeMsbuildGraphHash(actualGraph);
        var solutionHash = await WorkspaceCacheCoordinator.ComputeSolutionContentHashAsync(solutionPath, CancellationToken.None);
        Assert.IsNotNull(solutionHash);
        var sdkVersion = WorkspaceCacheCoordinator.ResolveSdkVersionForCacheKey();
        var staleEntry = new WorkspaceCacheEntry(
            new[] { new CachedProjectGraphNode(Path.Combine(_root, "Old.csproj"), Array.Empty<string>()) },
            Array.Empty<CachedProjectMetadataReferences>(),
            DateTime.UtcNow.AddMinutes(-5));
        var store = new WorkspaceCacheStore(_root);
        var coordinator = CreateCoordinator(store);
        var probe = new WorkspaceCacheProbe(solutionHash, sdkVersion, staleEntry, MetadataReferencesStillStable: true);

        var result = await coordinator.ResolveAndWriteAsync(solution, probe, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.CacheHit);
        var written = await store.TryGetAsync(new WorkspaceCacheKey(solutionHash, sdkVersion, actualGraphHash), CancellationToken.None);
        Assert.IsNotNull(written);
        Assert.AreEqual(projectPath, written.ProjectGraph.Single().ProjectPath);
    }

    [TestMethod]
    public async Task WriteFreshEntryAsync_WhenStoreThrows_ReturnsNullInsteadOfThrowing()
    {
        var solutionPath = WriteSolutionFile("throwing.slnx", "<Solution><Project Path=\"App.csproj\" /></Solution>");
        var solution = CreateSolution(Path.Combine(_root, "App.csproj"));
        var coordinator = CreateCoordinator(new ThrowingCacheStore());

        var result = await coordinator.WriteFreshEntryAsync(solution, solutionPath, CancellationToken.None);

        Assert.IsNull(result);
    }

    private string WriteSolutionFile(string fileName, string contents)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private static Solution CreateSolution(string projectPath)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "App",
            assemblyName: "App",
            language: LanguageNames.CSharp,
            filePath: projectPath);
        workspace.AddProject(projectInfo);
        return workspace.CurrentSolution;
    }

    private static WorkspaceCacheCoordinator CreateCoordinator(IWorkspaceCacheStore store) =>
        new(store, NullLogger<WorkspaceCacheCoordinator>.Instance);

    private sealed class ThrowingCacheStore : IWorkspaceCacheStore
    {
        public Task<WorkspaceCacheEntry?> TryGetAsync(WorkspaceCacheKey key, CancellationToken ct) =>
            throw new IOException("read failed");

        public Task<bool> PutAsync(WorkspaceCacheKey key, WorkspaceCacheEntry entry, CancellationToken ct) =>
            throw new IOException("write failed");

        public Task InvalidateAsync(WorkspaceCacheKey key, CancellationToken ct) =>
            throw new IOException("invalidate failed");
    }
}
