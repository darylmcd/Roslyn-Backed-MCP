using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

internal static class LocalWorkspaceManagerFixture
{
    internal static async Task RunAsync(
        Func<WorkspaceManager> createManager,
        Func<string> createSolutionCopy,
        int copyCount,
        Func<WorkspaceManager, IReadOnlyList<string>, Task> body,
        Action? additionalCleanup = null)
    {
        WorkspaceManager? manager = null;
        List<string> paths = [];
        List<Func<ValueTask>> cleanupSteps =
        [
            async () =>
            {
                if (manager is not null)
                    await manager.DisposeAsync();
            },
            CleanupFailureCollector.FromAction(() => additionalCleanup?.Invoke()),
            CleanupFailureCollector.FromAction(() =>
            {
                if (manager is not null)
                    Assert.AreEqual(0, manager.ListWorkspaces().Count,
                        "The local manager must finish shutdown before copied roots are deleted.");
            })
        ];

        await FixtureCleanupScope.RunAsync(
            async () =>
            {
                manager = createManager();
                for (var index = 0; index < copyCount; index++)
                {
                    var path = createSolutionCopy();
                    paths.Add(path);
                    cleanupSteps.Add(CleanupFailureCollector.FromAction(() =>
                        TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path)!)));
                }

                await body(manager, paths);
            },
            cleanupSteps);
    }
}
