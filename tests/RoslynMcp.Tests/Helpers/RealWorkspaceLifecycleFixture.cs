using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Runs a test body against a REAL <see cref="WorkspaceManager"/> over isolated SampleSolution copies,
/// so close/reload ordering and the WorkspaceClosed/WorkspaceReloaded events are production behavior,
/// not hand-raised. Used by the cache-lifetime tests.
/// </summary>
internal static class RealWorkspaceLifecycleFixture
{
    internal static Task RunAsync(int copyCount, Func<WorkspaceManager, IReadOnlyList<string>, Task> body)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var sampleSolution = TestFixtureFileSystem.FindFixturePath(
            repositoryRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
        return LocalWorkspaceManagerFixture.RunAsync(
            CreateManager,
            () => TestFixtureFileSystem.CreateSampleSolutionCopy(repositoryRoot, sampleSolution),
            copyCount,
            body);
    }

    /// <summary>
    /// Appends <paramref name="marker"/> to the first C# document via <c>TryApplyChanges</c>, which
    /// bumps the workspace version. Returns the version it moved from.
    /// </summary>
    internal static async Task<int> BumpVersionAsync(WorkspaceManager manager, string id, string marker)
    {
        var before = manager.GetCurrentVersion(id);
        var solution = manager.GetCurrentSolution(id);
        var document = solution.Projects.SelectMany(p => p.Documents).First(d => d.FilePath?.EndsWith(".cs") == true);
        var text = await document.GetTextAsync();
        var changed = solution.WithDocumentText(document.Id, SourceText.From(text + "\n" + marker));
        Assert.IsTrue(manager.TryApplyChanges(id, changed));
        Assert.IsTrue(manager.GetCurrentVersion(id) > before, "Fixture precondition: the apply must bump the version.");
        return before;
    }

    private static WorkspaceManager CreateManager() => new(
        NullLogger<WorkspaceManager>.Instance,
        new PreviewStore(),
        new FileWatcherService(NullLogger<FileWatcherService>.Instance),
        new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
}
