using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct unit tests for <see cref="CompilationCache"/>. Uses an in-memory
/// <see cref="AdhocWorkspace"/> so the cache can be exercised without spinning up a real
/// MSBuildWorkspace, and a tiny <see cref="IWorkspaceManager"/> stub so we can drive the
/// version counter directly to verify invalidation.
/// </summary>
[TestClass]
public sealed class CompilationCacheTests
{
    [TestMethod]
    public async Task GetCompilationAsync_ReturnsSameInstance_OnRepeatedCall_AtSameVersion()
    {
        var (cache, project, ws) = CreateCacheWithProject(initialVersion: 1);

        var first = await cache.GetCompilationAsync(ws.WorkspaceId, project, default);
        var second = await cache.GetCompilationAsync(ws.WorkspaceId, project, default);

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
        var projectId = ProjectId.CreateNewId();
        adhoc.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(),
            name: "TestProject", assemblyName: "TestProject",
            language: LanguageNames.CSharp));
        var v1Project = adhoc.CurrentSolution.GetProject(projectId)!;

        var ws = new FakeWorkspaceManager(workspaceId, initialVersion: 1);
        var cache = new CompilationCache(ws);

        var first = await cache.GetCompilationAsync(workspaceId, v1Project, default);
        Assert.IsNotNull(first);
        Assert.AreEqual(0, first!.SyntaxTrees.Count(), "Initial compilation should have no source files.");

        // Mutate the project: add a syntax tree and bump the workspace version.
        var docId = DocumentId.CreateNewId(projectId);
        var newSolution = adhoc.CurrentSolution.AddDocument(
            docId, "Sample.cs", "namespace N { class C { } }");
        var v2Project = newSolution.GetProject(projectId)!;
        ws.Version = 2;

        var second = await cache.GetCompilationAsync(workspaceId, v2Project, default);
        Assert.IsNotNull(second);
        Assert.AreEqual(1, second!.SyntaxTrees.Count(),
            "After version bump, cache must compile the new project state, not return the stale v1 entry.");
    }

    [TestMethod]
    public async Task GetCompilationWithAnalyzersAsync_ReturnsNull_WhenProjectHasNoAnalyzers()
    {
        var (cache, project, ws) = CreateCacheWithProject(initialVersion: 1);

        var bound = await cache.GetCompilationWithAnalyzersAsync(ws.WorkspaceId, project, default);

        Assert.IsNull(bound, "AdhocWorkspace projects have no AnalyzerReferences, so the cache must return null instead of fabricating an empty bound compilation.");
    }

    [TestMethod]
    public void Invalidate_IsSafeToCall_OnEmptyCache()
    {
        var ws = new FakeWorkspaceManager("test-ws", initialVersion: 1);
        var cache = new CompilationCache(ws);

        // Smoke test — Invalidate should be a no-op when nothing is cached for the workspace.
        // (Roslyn's Project-level Compilation cache makes a stronger "evicted-and-rebuilt"
        // assertion impossible without reflection, but the method must not throw.)
        cache.Invalidate("test-ws");
        cache.Invalidate("nonexistent-ws");
    }

    private static (CompilationCache cache, Project project, FakeWorkspaceManager ws) CreateCacheWithProject(int initialVersion)
    {
        const string workspaceId = "test-ws";
        var adhoc = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp);
        adhoc.AddProject(projectInfo);
        var project = adhoc.CurrentSolution.GetProject(projectId)!;

        var ws = new FakeWorkspaceManager(workspaceId, initialVersion);
        var cache = new CompilationCache(ws);
        return (cache, project, ws);
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in. Only the methods the cache
    /// actually calls (<see cref="GetCurrentVersion"/>) need real implementations; the rest
    /// throw to surface accidental coupling immediately.
    /// </summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public string WorkspaceId { get; }
        public int Version { get; set; }

        public event Action<string>? WorkspaceClosed;

        public FakeWorkspaceManager(string workspaceId, int initialVersion)
        {
            WorkspaceId = workspaceId;
            Version = initialVersion;
        }

        public void RaiseWorkspaceClosed(string workspaceId) => WorkspaceClosed?.Invoke(workspaceId);

        public int GetCurrentVersion(string workspaceId) => Version;
        public void RestoreVersion(string workspaceId, int version) => Version = version;

        // ----- Unused by CompilationCache; throw to surface unexpected coupling -----
        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => workspaceId == WorkspaceId;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
