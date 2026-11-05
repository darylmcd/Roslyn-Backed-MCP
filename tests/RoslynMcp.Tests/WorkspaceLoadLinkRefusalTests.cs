using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the load-time physical-path refusal that runs after the workspace is opened and before any
/// session state is published (<c>workspace-manager-load-finalization-collaborator-extraction</c>).
/// A project document reached through a filesystem link whose target is still inside the project
/// directory must fail the load with the link-component message, register no session on an initial
/// load, and leave the prior session untouched on a reload.
/// </summary>
// Private manager per test (same isolation rationale as WorkspaceLoadCleanupGateReleaseTests); each test owns a
// unique CreateSampleSolutionCopy tree and the link it creates inside it.
[TestClass]
public sealed class WorkspaceLoadLinkRefusalTests
{
    private const string LinkComponentMessage = "filesystem-link component";

    private static string _repoRoot = null!;
    private static string _sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        _sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            _repoRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    private static WorkspaceManager CreateManager() =>
        new(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 },
            cacheStore: null,
            sessionLoader: null,
            ownsFileWatcher: true);

    private static CancellationToken Bounded(int seconds = 120) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    /// <summary>
    /// Creates <c>SampleLib/LinkedHierarchy</c> pointing at the sibling <c>SampleLib/Hierarchy</c>, so the SDK
    /// glob enumerates documents through the link while their physical paths stay under the project directory.
    /// </summary>
    private static string? TryCreateLinkInsideProject(string copiedRoot)
    {
        var projectDirectory = Path.Combine(copiedRoot, "SampleLib");
        var link = Path.Combine(projectDirectory, "LinkedHierarchy");
        return TestFixtureFileSystem.TryCreateDirectoryLink(link, Path.Combine(projectDirectory, "Hierarchy"))
            ? link
            : null;
    }

    [TestMethod]
    public async Task Load_ProjectDocumentReachedThroughDirectoryLink_Refuses_AndRegistersNothing()
    {
        var copiedSolutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(_repoRoot, _sampleSolutionPath);
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            using var manager = CreateManager();
            if (TryCreateLinkInsideProject(copiedRoot) is null)
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => manager.LoadAsync(copiedSolutionPath, Bounded()));

            StringAssert.Contains(thrown.Message, LinkComponentMessage);
            Assert.AreEqual(0, manager.ListWorkspaces().Count, "A refused initial load must not register a session.");
            Assert.AreEqual(
                0,
                manager.FindWorkspaceIdsContainingFile(Path.Combine(copiedRoot, "SampleLib", "AnimalService.cs")).Count,
                "A refused initial load must not publish a document-path index.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Reload_ProjectDocumentReachedThroughDirectoryLink_Refuses_AndPriorSessionStaysObservable()
    {
        var copiedSolutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(_repoRoot, _sampleSolutionPath);
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            using var manager = CreateManager();
            var loaded = await manager.LoadAsync(copiedSolutionPath, Bounded());
            var solutionBefore = manager.GetCurrentSolution(loaded.WorkspaceId);
            var before = manager.GetStatus(loaded.WorkspaceId);
            var animalService = Path.Combine(copiedRoot, "SampleLib", "AnimalService.cs");

            var link = TryCreateLinkInsideProject(copiedRoot);
            if (link is null)
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => manager.ReloadAsync(loaded.WorkspaceId, Bounded()));

            StringAssert.Contains(thrown.Message, LinkComponentMessage);
            var after = manager.GetStatus(loaded.WorkspaceId);
            Assert.IsTrue(after.IsLoaded);
            Assert.AreEqual(before.WorkspaceVersion, after.WorkspaceVersion, "A refused reload must not swap.");
            Assert.AreEqual(before.LoadedAtUtc, after.LoadedAtUtc);
            Assert.AreEqual(before.ProjectCount, after.ProjectCount);
            Assert.AreEqual(before.DocumentCount, after.DocumentCount);
            CollectionAssert.AreEqual(before.Projects.ToArray(), after.Projects.ToArray());
            Assert.AreSame(solutionBefore, manager.GetCurrentSolution(loaded.WorkspaceId),
                "The prior loaded workspace must remain the observable one.");
            CollectionAssert.AreEqual(
                new[] { loaded.WorkspaceId },
                manager.FindWorkspaceIdsContainingFile(animalService).ToArray(),
                "The prior document-path index must remain published.");

            // Removing the link only deletes the reparse point; the load lock must be free and the reload must now commit.
            Directory.Delete(link);
            var recovered = await manager.ReloadAsync(loaded.WorkspaceId, Bounded(60));
            Assert.AreEqual(before.WorkspaceVersion + 1, recovered.WorkspaceVersion);
            Assert.AreEqual(before.DocumentCount, recovered.DocumentCount);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
