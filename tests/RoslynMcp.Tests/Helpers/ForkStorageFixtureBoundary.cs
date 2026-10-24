using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

internal static class ForkStorageFixtureBoundary
{
    internal static Task<string> ValidateAsync(string workspaceId, string path, CancellationToken ct) =>
        ClientRootPathValidator.ValidatePathAgainstRootsAsync(null, path, ct,
            securityOptions: new SecurityOptions
            {
                SanctionedRoots = [TestTempRoot.Current, TestFixtureFileSystem.FindRepositoryRoot()],
            });
}
