using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceProjectIndexLifetimeTests
{
    private const string ProjectName = "Entry";

    [TestMethod]
    public async Task Close_ReleasesProjectIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNotNull(fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName));
        var slot = fixture.Manager.CaptureProjectIndexSlotForTesting(fixture.WorkspaceId);
        Assert.IsNotNull(slot()?.Solution, "A live lookup publishes one index on the session.");
        Assert.AreEqual(1, fixture.Manager.ProjectIndexCountForTesting);

        Assert.IsTrue(fixture.Manager.Close(fixture.WorkspaceId));

        AssertSlotClosed(slot, "Close must release the closed session's own index.");
        Assert.AreEqual(0, fixture.Manager.ProjectIndexCountForTesting);
    }

    [TestMethod]
    public async Task Dispose_ReleasesProjectIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNotNull(fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName));

        var slot = fixture.Manager.CaptureProjectIndexSlotForTesting(fixture.WorkspaceId);
        Assert.IsNotNull(slot()?.Solution);

        await fixture.Manager.DisposeAsync();

        AssertSlotClosed(slot, "Manager disposal must release every session's own index.");
        Assert.AreEqual(0, fixture.Manager.ProjectIndexCountForTesting);
    }

    [TestMethod]
    public async Task CloseBetweenLookupAndPublish_DoesNotRepublishClosedSessionIndex()
    {
        await using var fixture = await Fixture.CreateAsync();
        var slot = fixture.Manager.CaptureProjectIndexSlotForTesting(fixture.WorkspaceId);
        fixture.Manager.BeforeProjectIndexPublishForTesting = () =>
        {
            fixture.Manager.BeforeProjectIndexPublishForTesting = null;
            Assert.IsTrue(fixture.Manager.Close(fixture.WorkspaceId));
        };

        // The reader started before the close, so it may still answer from its own snapshot.
        fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);

        AssertSlotClosed(slot, "A reader whose publish overlaps close must not write into the closed session.");
    }

    [TestMethod]
    public async Task CloseBetweenSolutionReadAndPublish_DoesNotPublishIntoClosedSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var slot = fixture.Manager.CaptureProjectIndexSlotForTesting(fixture.WorkspaceId);
        fixture.Manager.AfterProjectIndexSolutionReadForTesting = () =>
        {
            fixture.Manager.AfterProjectIndexSolutionReadForTesting = null;
            Assert.IsTrue(fixture.Manager.Close(fixture.WorkspaceId));
        };

        // The reader observes the already-closed slot after its solution read.
        fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);

        AssertSlotClosed(slot, "A reader that observes a closed session must not publish.");
    }

    private static void AssertSlotClosed(Func<WorkspaceManager.ProjectIndexEntry?> slot, string message)
    {
        var entry = slot();
        Assert.IsNotNull(entry, message + " Slot is empty instead of terminal.");
        Assert.IsNull(entry.Solution, message + " Slot still holds a Solution snapshot.");
        Assert.AreSame(WorkspaceManager.ProjectIndexEntry.Closed, entry, message);
    }

    [TestMethod]
    public async Task LiveSession_CacheHitBuildsOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        var second = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, fixture.Manager.ProjectIndexBuildCountForTesting);
        Assert.AreEqual(1, fixture.Manager.ProjectIndexCountForTesting);
    }

    [TestMethod]
    public async Task TryApplyChanges_ReplacesSolution_RebuildsExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        Assert.IsNotNull(before);

        var solution = fixture.Manager.GetCurrentSolution(fixture.WorkspaceId);
        var document = solution.Projects.Single().Documents.Single(d => d.FilePath == fixture.SourcePath);
        var edited = solution.WithDocumentText(document.Id, SourceText.From("public class Entry { public int Changed; }"));
        Assert.IsTrue(fixture.Manager.TryApplyChanges(fixture.WorkspaceId, edited));

        var after = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        var again = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);

        Assert.IsNotNull(after);
        Assert.AreNotSame(before, after);
        Assert.AreSame(fixture.Manager.GetCurrentSolution(fixture.WorkspaceId), after.Solution);
        Assert.AreSame(after, again);
        Assert.AreEqual(2, fixture.Manager.ProjectIndexBuildCountForTesting, "One build per distinct solution.");
    }

    [TestMethod]
    public async Task Reload_ReplacesSolution_RebuildsExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        Assert.IsNotNull(before);

        await fixture.Manager.ReloadAsync(fixture.WorkspaceId, fixture.Token);
        var after = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        var again = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);

        Assert.IsNotNull(after);
        Assert.AreNotSame(before, after);
        Assert.AreSame(after, again);
        Assert.AreEqual(2, fixture.Manager.ProjectIndexBuildCountForTesting);
    }

    [TestMethod]
    public async Task ReloadBetweenSolutionReadAndPublish_DoesNotServeOldSolutionForNewSolution()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Manager.AfterProjectIndexSolutionReadForTesting = () =>
        {
            fixture.Manager.AfterProjectIndexSolutionReadForTesting = null;
            fixture.Manager.ReloadAsync(fixture.WorkspaceId, fixture.Token).GetAwaiter().GetResult();
        };

        // This reader captured the pre-reload solution; its own answer may be that snapshot.
        fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);

        var fresh = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        Assert.IsNotNull(fresh);
        Assert.AreSame(fixture.Manager.GetCurrentSolution(fixture.WorkspaceId), fresh.Solution,
            "A lookup after the reload must be answered from the reloaded solution.");
    }

    [TestMethod]
    public async Task ReopenedSession_DoesNotConsumeClosedLifetimeSnapshots()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Manager.GetProject(fixture.WorkspaceId, ProjectName);
        Assert.IsNotNull(first);
        var firstSlot = fixture.Manager.CaptureProjectIndexSlotForTesting(fixture.WorkspaceId);
        Assert.IsTrue(fixture.Manager.Close(fixture.WorkspaceId));
        AssertSlotClosed(firstSlot, "The closed lifetime must release its snapshots.");

        var reopened = await fixture.Manager.LoadAsync(fixture.ProjectPath, fixture.Token);
        var second = fixture.Manager.GetProject(reopened.WorkspaceId, ProjectName);

        Assert.IsNotNull(second);
        Assert.AreNotSame(first, second);
        Assert.AreSame(fixture.Manager.GetCurrentSolution(reopened.WorkspaceId), second.Solution);
        Assert.AreEqual(1, fixture.Manager.ProjectIndexCountForTesting);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly FileWatcherService _watcher;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(2));

        private Fixture(string root, FileWatcherService watcher, WorkspaceManager manager)
        {
            _root = root;
            _watcher = watcher;
            Manager = manager;
            ProjectPath = Path.Combine(root, ProjectName + ".csproj");
            SourcePath = Path.Combine(root, "Entry.cs");
        }

        public WorkspaceManager Manager { get; }
        public string ProjectPath { get; }
        public string SourcePath { get; }
        public string WorkspaceId { get; private set; } = string.Empty;
        public CancellationToken Token => _timeout.Token;

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(TestTempRoot.Current, "project-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ProjectName + ".csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Entry.cs"), "public class Entry { }");
            var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            var manager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, new PreviewStore(), watcher,
                options: null, cacheStore: null, sessionLoader: null, ownsFileWatcher: false);
            var fixture = new Fixture(root, watcher, manager);
            var loaded = await manager.LoadAsync(fixture.ProjectPath, fixture.Token);
            fixture.WorkspaceId = loaded.WorkspaceId;
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            _watcher.Dispose();
            _timeout.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
        }
    }
}
