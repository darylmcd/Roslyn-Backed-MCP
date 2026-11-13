using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// test-discovery-cache-session-lifetime: the discovery cache must not outlive its workspace. Every
/// test drives a REAL <see cref="WorkspaceManager"/> (real close ordering and lifecycle events) through
/// <see cref="LifecycleHookWorkspaceManager"/> so a close lands at a deterministic point in discovery.
/// Tests marked CONTROL pass before and after the fix; they pin behavior the fix must preserve.
/// </summary>
[TestClass]
public sealed class TestDiscoveryCacheLifetimeTests
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);

    private static TestDiscoveryService CreateService(IWorkspaceManager manager) =>
        new(manager, NullLogger<TestDiscoveryService>.Instance);

    [TestMethod]
    public Task Close_EvictsTheCachedEntry() => RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
    {
        var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
        var service = CreateService(manager);

        await service.DiscoverTestsAsync(id, CancellationToken.None);
        Assert.AreEqual(1, service.CachedWorkspaceCount, "Fixture precondition: discovery must populate the cache.");

        Assert.IsTrue(manager.Close(id));

        Assert.AreEqual(0, service.CachedWorkspaceCount);
    });

    [TestMethod]
    public Task Reload_EvictsTheCachedEntry() => RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
    {
        var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
        var service = CreateService(manager);
        await service.DiscoverTestsAsync(id, CancellationToken.None);
        Assert.AreEqual(1, service.CachedWorkspaceCount, "Fixture precondition: discovery must populate the cache.");

        await manager.ReloadAsync(id, CancellationToken.None);

        Assert.AreEqual(0, service.CachedWorkspaceCount);
    });

    [TestMethod]
    public Task CloseWhileDiscoveryIsInFlight_DoesNotRepublishTheClosedSession() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var hooks = new LifecycleHookWorkspaceManager(manager);
            var service = CreateService(hooks);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            hooks.AfterGetStatusAsync = async _ =>
            {
                reached.TrySetResult();
                await release.Task;
            };

            var discovery = service.DiscoverTestsAsync(id, CancellationToken.None);
            await reached.Task.WaitAsync(GateTimeout);
            Assert.IsTrue(manager.Close(id), "The close must complete while discovery is suspended.");
            release.SetResult();
            var result = await discovery.WaitAsync(GateTimeout);

            Assert.IsNotNull(result, "The in-flight caller still receives its result.");
            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task CloseInsideDiscoveryContinuation_LeavesNoEntry() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var hooks = new LifecycleHookWorkspaceManager(manager);
            var service = CreateService(hooks);
            var closed = false;
            // The close (session removal, then WorkspaceClosed) runs entirely inside discovery's own
            // continuation: no cache entry exists yet when the event fires, so only the publish-time
            // liveness check can prevent a republish.
            hooks.AfterGetStatusAsync = _ =>
            {
                closed = manager.Close(id);
                return Task.CompletedTask;
            };

            await service.DiscoverTestsAsync(id, CancellationToken.None);

            Assert.IsTrue(closed);
            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task RepeatedLoadDiscoverCloseCycles_DoNotAccumulateEntries() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var service = CreateService(manager);

            for (var cycle = 0; cycle < 3; cycle++)
            {
                var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
                await service.DiscoverTestsAsync(id, CancellationToken.None);
                Assert.AreEqual(1, service.CachedWorkspaceCount, $"cycle {cycle}: only the live session may be cached.");
                Assert.IsTrue(manager.Close(id));
            }

            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task Control_CacheHitSurvivesUnrelatedWorkspaceClose() =>
        RealWorkspaceLifecycleFixture.RunAsync(2, async (manager, paths) =>
        {
            var keep = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var other = (await manager.LoadAsync(paths[1], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var service = CreateService(manager);
            var first = await service.DiscoverTestsAsync(keep, CancellationToken.None);
            await service.DiscoverTestsAsync(other, CancellationToken.None);

            Assert.IsTrue(manager.Close(other));

            Assert.AreSame(first, await service.DiscoverTestsAsync(keep, CancellationToken.None),
                "An unchanged version keeps serving the cached result after a sibling closes.");
        });

    [TestMethod]
    public Task Control_ReopenedSessionCannotReadAnEarlierLifetimesEntry() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var firstId = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var service = CreateService(manager);
            var firstResult = await service.DiscoverTestsAsync(firstId, CancellationToken.None);
            Assert.IsTrue(manager.Close(firstId));

            var secondId = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;

            Assert.AreNotEqual(firstId, secondId, "A reopened session must mint a fresh workspace id.");
            // The closed id is refused (not-found or evicted, both KeyNotFoundException) before any cache read.
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.DiscoverTestsAsync(firstId, CancellationToken.None));
            var secondResult = await service.DiscoverTestsAsync(secondId, CancellationToken.None);
            Assert.AreNotSame(firstResult, secondResult);
        });

    // CONTROL (non-red): passes on the pre-fix code too; pins that eviction/liveness checks keep versioned hits.
    [TestMethod]
    public Task Control_VersionBumpMissesThenRecachesAndHitsAtTheNewVersion() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var service = CreateService(manager);
            var atV1 = await service.DiscoverTestsAsync(id, CancellationToken.None);

            await RealWorkspaceLifecycleFixture.BumpVersionAsync(manager, id, "// bump");
            var atV2 = await service.DiscoverTestsAsync(id, CancellationToken.None);
            Assert.AreNotSame(atV1, atV2, "A version bump must miss.");
            Assert.AreSame(atV2, await service.DiscoverTestsAsync(id, CancellationToken.None));

            await manager.ReloadAsync(id, CancellationToken.None);
            var afterReload = await service.DiscoverTestsAsync(id, CancellationToken.None);
            Assert.AreNotSame(atV2, afterReload, "A reload must miss.");
            Assert.AreSame(afterReload, await service.DiscoverTestsAsync(id, CancellationToken.None));
            Assert.AreEqual(1, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task Dispose_DetachesBothHandlersAndIsIdempotent() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var service = CreateService(manager);
            await service.DiscoverTestsAsync(id, CancellationToken.None);
            Assert.AreEqual(1, service.CachedWorkspaceCount);

            service.Dispose();
            service.Dispose();

            await manager.ReloadAsync(id, CancellationToken.None);
            Assert.AreEqual(1, service.CachedWorkspaceCount, "A detached service must ignore WorkspaceReloaded.");
            Assert.IsTrue(manager.Close(id));
            Assert.AreEqual(1, service.CachedWorkspaceCount, "A detached service must ignore WorkspaceClosed.");
        });
}
