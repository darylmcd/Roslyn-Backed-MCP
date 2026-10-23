using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Owns the complete production service graph for one test invocation. Mutable workspaces,
/// preview capacity, undo state and watchers cannot interfere with another test's graph.
/// </summary>
public abstract class OwnedRefactoringTestBase
{
    private TestServiceContainer? _services;
    private readonly List<string> _roots = [];

    private protected TestServiceContainer Services => _services
        ?? throw new InvalidOperationException("The owned test services have not been initialized.");

    [TestInitialize]
    public void InitializeOwnedServices()
    {
        MsBuildInitializer.EnsureInitialized();
        _services = TestServiceContainer.Create(TestBase.BindValidationOptionsFromEnvironment(), new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy()));
    }

    [TestCleanup]
    public async Task CleanupOwnedServicesAsync()
    {
        var services = _services;
        _services = null;
        var steps = new List<Func<ValueTask>>();
        if (services is not null)
        {
            foreach (var workspace in services.WorkspaceManager.ListWorkspaces())
                steps.Add(CleanupFailureCollector.FromAction(() => services.WorkspaceManager.Close(workspace.WorkspaceId)));
            steps.Add(CleanupFailureCollector.FromAction(services.Dispose));
        }
        foreach (var root in _roots)
            steps.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        _roots.Clear();
        await CleanupFailureCollector.RunAsync("Owned refactoring fixture cleanup failed.", steps.ToArray());
    }

    protected string CreateSampleSolutionCopy()
    {
        var fixtures = TestRepositoryFixtures.Discover();
        var path = TestFixtureFileSystem.CreateSampleSolutionCopy(fixtures.RepositoryRootPath, fixtures.SampleSolutionPath);
        _roots.Add(Path.GetDirectoryName(path)!);
        return path;
    }

    private protected IsolatedWorkspaceTestBase.IsolatedWorkspaceScope CreateSolutionWorkspace()
    {
        var path = CreateSampleSolutionCopy();
        return IsolatedWorkspaceTestBase.CreateWorkspaceScope(
            Path.GetDirectoryName(path)!, path, Services.WorkspaceManager);
    }

    private protected IsolatedWorkspaceTestBase.IsolatedWorkspaceScope CreateSingleProjectWorkspace() =>
        CreateCopiedWorkspace(TestFixtureFileSystem.CreateSampleLibraryCopy);

    private protected IsolatedWorkspaceTestBase.IsolatedWorkspaceScope CreateLibraryTestsWorkspace() =>
        CreateCopiedWorkspace(TestFixtureFileSystem.CreateSampleLibraryTestsCopy);

    private IsolatedWorkspaceTestBase.IsolatedWorkspaceScope CreateCopiedWorkspace(
        Func<string, string, (string RootPath, string EntryPath)> copy)
    {
        var fixtures = TestRepositoryFixtures.Discover();
        var (root, entryPath) = copy(fixtures.RepositoryRootPath, fixtures.SampleSolutionPath);
        _roots.Add(root);
        return IsolatedWorkspaceTestBase.CreateWorkspaceScope(root, entryPath, Services.WorkspaceManager);
    }
}
