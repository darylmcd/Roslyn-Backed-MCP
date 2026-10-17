using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringFixtureOwnershipTests
{
    [TestMethod]
    public async Task OtherFixturePreviewPressure_DoesNotEvictLiveTokenAsync()
    {
        var first = new Probe();
        var second = new Probe();
        try
        {
            first.InitializeOwnedServices();
            second.InitializeOwnedServices();
            using var workspace = new AdhocWorkspace();
            var token = first.Store.Store("first", workspace.CurrentSolution, 1, "live preview");
            var secondTokens = new List<string>();
            for (var i = 0; i < 64; i++)
                secondTokens.Add(second.Store.Store("second", workspace.CurrentSolution, 1, "unrelated preview"));
            Assert.IsNotNull(first.Store.Retrieve(token), "Another fixture must not evict this fixture's live token.");
            Assert.IsTrue(secondTokens.Any(candidate => second.Store.Retrieve(candidate) is null),
                "The owning fixture must retain production eviction behavior.");
        }
        finally
        {
            await CleanupFailureCollector.RunAsync("Probe cleanup failed.",
                () => new ValueTask(first.CleanupOwnedServicesAsync()),
                () => new ValueTask(second.CleanupOwnedServicesAsync()));
        }
    }

    [TestMethod]
    public async Task SingleProjectFixture_ReloadAndCleanupUseItsOwningManagerAsync()
    {
        var fixture = new Probe();
        try
        {
            fixture.InitializeOwnedServices();
            var scope = fixture.CreateWorkspace();
            var workspaceId = await scope.LoadAsync();
            var manager = fixture.Manager;
            var store = fixture.Store;
            var solution = manager.GetCurrentSolution(workspaceId);
            Assert.AreEqual(1, solution.ProjectIds.Count);
            var project = solution.Projects.Single();
            Assert.AreEqual("SampleLib", project.Name);
            var compilation = await project.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            Assert.AreNotEqual(TypeKind.Error, compilation.GetSpecialType(SpecialType.System_String).TypeKind);
            Assert.IsFalse(compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error));

            var token = store.Store(workspaceId, solution, manager.GetCurrentVersion(workspaceId), "reload boundary");
            await scope.ReloadAsync();
            Assert.IsNotNull(store.Retrieve(token), "One reload remains inside the production version span.");
            await scope.ReloadAsync();
            Assert.IsNull(store.Retrieve(token), "The owning manager must invalidate previews beyond their version span.");

            var closingToken = store.Store(workspaceId, manager.GetCurrentSolution(workspaceId),
                manager.GetCurrentVersion(workspaceId), "close boundary");
            bool? rootExistedAtClose = null;
            manager.WorkspaceClosed += id =>
            {
                if (id == workspaceId)
                    rootExistedAtClose = Directory.Exists(scope.RootPath);
            };
            await fixture.CleanupOwnedServicesAsync();
            Assert.AreEqual(true, rootExistedAtClose, "Workspace close must precede directory deletion.");
            Assert.IsNull(store.Retrieve(closingToken));
            Assert.IsFalse(Directory.Exists(scope.RootPath));
            await fixture.CleanupOwnedServicesAsync();
        }
        finally
        {
            await fixture.CleanupOwnedServicesAsync();
        }
    }

    [TestMethod]
    public async Task Cleanup_RemovesCopiedRootWhenSetupStopsBeforeLoadAsync()
    {
        var fixture = new Probe();
        try
        {
            fixture.InitializeOwnedServices();
            var scope = fixture.CreateWorkspace();
            Assert.IsTrue(Directory.Exists(scope.RootPath));
            await fixture.CleanupOwnedServicesAsync();
            Assert.IsFalse(Directory.Exists(scope.RootPath));
        }
        finally
        {
            await fixture.CleanupOwnedServicesAsync();
        }
    }

    private sealed class Probe : OwnedRefactoringTestBase
    {
        public IPreviewStore Store => Services.PreviewStore;
        public WorkspaceManager Manager => Services.WorkspaceManager;
        public IsolatedWorkspaceTestBase.IsolatedWorkspaceScope CreateWorkspace() => CreateSingleProjectWorkspace();
    }
}
