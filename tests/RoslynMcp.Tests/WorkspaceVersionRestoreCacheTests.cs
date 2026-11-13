using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// undo-restore-version-lowering-version-keyed-caches: <c>RestoreVersion</c> lowers the workspace
/// version, so a version-keyed cache entry written at the higher (reverted) version would hit again
/// when a later, different apply reuses that number. Lowering must evict through the same
/// <c>WorkspaceReloaded</c> event a reload uses. Every test drives a REAL <see cref="WorkspaceManager"/>.
/// </summary>
[TestClass]
public sealed class WorkspaceVersionRestoreCacheTests
{
    [TestMethod]
    public Task RestoreVersion_EvictsTheVersionKeyedDiscoveryCache() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            using var service = new TestDiscoveryService(manager, NullLogger<TestDiscoveryService>.Instance);
            var lowered = await RealWorkspaceLifecycleFixture.BumpVersionAsync(manager, id, "// apply A");
            await service.DiscoverTestsAsync(id, CancellationToken.None);
            Assert.AreEqual(1, service.CachedWorkspaceCount, "Fixture precondition: discovery must populate the cache.");

            manager.RestoreVersion(id, lowered);

            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task RestoreVersion_ThenDifferentApplyAtTheSameVersion_DoesNotServeTheRevertedDiscoveryResult() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            using var service = new TestDiscoveryService(manager, NullLogger<TestDiscoveryService>.Instance);
            var lowered = await RealWorkspaceLifecycleFixture.BumpVersionAsync(manager, id, "// apply A");
            var atApplyA = await service.DiscoverTestsAsync(id, CancellationToken.None);

            manager.RestoreVersion(id, lowered);
            await RealWorkspaceLifecycleFixture.BumpVersionAsync(manager, id, "// apply B");
            var atApplyB = await service.DiscoverTestsAsync(id, CancellationToken.None);

            Assert.AreNotSame(atApplyA, atApplyB,
                "A different apply that reuses the restored version number must not hit the reverted apply's cached result.");
        });

    [TestMethod]
    public Task RestoreVersion_ThenDifferentApplyAtTheSameVersion_DoesNotServeTheRevertedCompilation() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var builds = 0;
            using var cache = new CompilationCache(
                manager,
                project =>
                {
                    Interlocked.Increment(ref builds);
                    return project.GetCompilationAsync(CancellationToken.None);
                },
                analyzerFactory: null);
            var lowered = await RealWorkspaceLifecycleFixture.BumpVersionAsync(manager, id, "// apply A");
            var projectA = manager.GetCurrentSolution(id).Projects.First();
            await cache.GetCompilationAsync(id, projectA, CancellationToken.None);

            manager.RestoreVersion(id, lowered);
            await RealWorkspaceLifecycleFixture.BumpVersionAsync(manager, id, "// apply B");
            var projectB = manager.GetCurrentSolution(id).Projects.First(p => p.Id == projectA.Id);
            await cache.GetCompilationAsync(id, projectB, CancellationToken.None);

            Assert.AreEqual(2, builds, "The restored version number must not hit the reverted apply's cached compilation.");
        });

    [TestMethod]
    public Task Revert_WithNoRestoreWork_DoesNotLowerTheVersion() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            // The only changed document has no file path (no disk restore) and the workspace apply is
            // refused, so the revert did nothing: the version must not be lowered under live caches.
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            var project = manager.GetCurrentSolution(id).Projects.First();
            var preApply = project.AddDocument("Mem.cs", "class Mem { }\n").Project.Solution;
            var memId = preApply.Projects.First(p => p.Id == project.Id).Documents.Single(d => d.Name == "Mem.cs").Id;
            var applied = preApply.WithDocumentText(memId, SourceText.From("class Mem { int x; }\n"));
            var fake = new RefusingApplyWorkspaceManager(manager, applied);
            using var undo = new UndoService(NullLogger<UndoService>.Instance, fake);
            undo.CaptureBeforeApply(id, "in-memory edit", preApply, fileSnapshots: null);

            Assert.IsFalse(await undo.RevertAsync(id, CancellationToken.None));

            Assert.AreEqual(0, fake.RestoreVersionCalls, "A revert that restored nothing must not lower the workspace version.");
            Assert.IsNotNull(undo.GetLastOperation(id), "The failed revert stays retryable.");
        });

    private sealed class RefusingApplyWorkspaceManager(IWorkspaceManager inner, Solution current)
        : DelegatingWorkspaceManager(inner)
    {
        public int RestoreVersionCalls { get; private set; }

        public override Solution GetCurrentSolution(string workspaceId) => current;

        public override bool TryApplyChanges(string workspaceId, Solution newSolution) => false;

        public override void RestoreVersion(string workspaceId, int version)
        {
            RestoreVersionCalls++;
            base.RestoreVersion(workspaceId, version);
        }
    }
}
