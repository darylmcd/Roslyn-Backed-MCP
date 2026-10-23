using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Exercises the production physical transaction for tests that require an applied snapshot.</summary>
internal static class PhysicalMutationFixture
{
    internal static async Task ApplyAsync(IWorkspaceManager manager, string workspaceId, Solution modified, CancellationToken ct)
    {
        var original = manager.GetCurrentSolution(workspaceId);
        var authority = (IWorkspaceRecoveryAuthority)manager;
        var paths = SolutionFileMutationPlanner.ChangedPaths(original, modified);
        await authority.AuthorizePathsAsync(workspaceId, paths, ct);
        var files = new List<RoslynMcp.Core.Services.FileSnapshotDto>();
        foreach (var path in paths)
            files.Add(await FileSnapshotCapture.CaptureAsync(path, fallbackTextFactory: null, ct));
        var operation = await authority.CaptureRecoveryAsync(workspaceId, "Fixture physical mutation", files, ct);
        var result = await new DocumentSetPersistenceService(manager, NullLogger.Instance).PersistAsync(
            workspaceId, original, modified, modified.GetChanges(original), files, operation, ct);
        Assert.IsTrue(result.Success, "The production physical transaction must apply the fixture snapshot.");
    }
}
