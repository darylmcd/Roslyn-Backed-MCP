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

    private static WorkspaceManager CreateManager() => new(
        NullLogger<WorkspaceManager>.Instance,
        new PreviewStore(),
        new FileWatcherService(NullLogger<FileWatcherService>.Instance),
        new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
}
