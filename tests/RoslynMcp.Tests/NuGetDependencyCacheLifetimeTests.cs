using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// test-discovery-cache-session-lifetime (same-mechanism instance): the vulnerability-scan cache
/// bucket is created after the workspace version is read; a close landing in that window must not
/// leave a bucket behind for a workspace that no longer exists.
/// </summary>
[TestClass]
public sealed class NuGetDependencyCacheLifetimeTests
{
    [TestMethod]
    public Task CloseBetweenVersionReadAndBucketCreation_LeavesNoBucket() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var hooks = new LifecycleHookWorkspaceManager(manager);
            var service = new NuGetDependencyService(
                hooks,
                new NeverExecutingGatedCommandExecutor(),
                new MsBuildEvaluationService(manager),
                NullLogger<NuGetDependencyService>.Instance);
            var closed = false;
            hooks.AfterGetCurrentVersion = workspaceId => closed = manager.Close(workspaceId);

            var plan = await service.PrepareVulnerabilityScanAsync(id, null, false, CancellationToken.None);

            Assert.IsTrue(closed);
            Assert.IsNull(plan.Cached);
            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task Control_LiveWorkspaceKeepsItsBucketAndCloseEvictsIt() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var service = new NuGetDependencyService(
                manager,
                new NeverExecutingGatedCommandExecutor(),
                new MsBuildEvaluationService(manager),
                NullLogger<NuGetDependencyService>.Instance);

            await service.PrepareVulnerabilityScanAsync(id, null, false, CancellationToken.None);
            Assert.AreEqual(1, service.CachedWorkspaceCount);

            Assert.IsTrue(manager.Close(id));
            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    private sealed class NeverExecutingGatedCommandExecutor : IGatedCommandExecutor
    {
        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
            CancellationToken ct) =>
            throw new NotSupportedException("The scan is only prepared, never executed.");

        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) =>
            throw new NotSupportedException("No project filter is used.");

        public void Dispose()
        {
        }
    }
}
