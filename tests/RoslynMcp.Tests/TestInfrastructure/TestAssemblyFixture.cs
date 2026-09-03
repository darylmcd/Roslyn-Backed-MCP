using ModelContextProtocol.Server;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Repository and sample-fixture paths discovered once per test assembly. Path discovery is a
/// separate concern from service lookup, so it lives in its own immutable record instead of
/// being welded onto <see cref="TestBase"/> as four more mutable statics.
/// </summary>
internal sealed record TestRepositoryFixtures(
    string RepositoryRootPath,
    string SampleSolutionPath,
    string BuildFailureSolutionPath,
    string GeneratedDocumentSolutionPath)
{
    public static TestRepositoryFixtures Discover()
    {
        var repositoryRootPath = TestFixtureFileSystem.FindRepositoryRoot();
        return new TestRepositoryFixtures(
            repositoryRootPath,
            TestFixtureFileSystem.FindFixturePath(repositoryRootPath, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln"),
            TestFixtureFileSystem.FindFixturePath(repositoryRootPath, "BuildFailureSolution", "BuildFailureSolution.slnx", "BuildFailureSolution.sln"),
            TestFixtureFileSystem.FindFixturePath(repositoryRootPath, "GeneratedDocumentSolution", "GeneratedDocumentSolution.slnx", "GeneratedDocumentSolution.sln"));
    }
}

/// <summary>
/// The single assembly-owned test context: the immutable <see cref="TestServiceContainer"/>, the
/// repository fixture paths, the shared <see cref="WorkspaceIdCache"/>, and the lazily-created
/// path-authorized MCP server session.
/// <para>
/// This type exists so initialization and disposal have exactly ONE owner.
/// <see cref="TestBase"/> previously declared 67 mutable statics that were hand-copied from the
/// container after construction, and hand-rolled its own assembly disposal because nothing owned
/// the resources. <see cref="TestBase"/>'s properties are now get-only forwarders over this
/// instance, so a new service is declared once in <see cref="TestServiceContainer"/> instead of
/// three times.
/// </para>
/// <para>
/// Lifetime is still governed by <see cref="TestBase.InitializeServices"/>'s once-per-assembly
/// double-checked gate; this type deliberately does NOT add a second initialization path.
/// </para>
/// </summary>
internal sealed class TestAssemblyFixture : IAsyncDisposable
{
    private readonly object _sessionLock = new();
    private Task<McpRootsTestServerFactory.Session>? _pathAuthorizedServerTask;
    private int _disposeState;
    private int _ownedResourceDisposalCount;

    internal TestAssemblyFixture(
        TestServiceContainer services,
        TestRepositoryFixtures repositoryFixtures)
    {
        Services = services;
        RepositoryFixtures = repositoryFixtures;
    }

    /// <summary>The immutable service container. The single declaration site for a test service.</summary>
    public TestServiceContainer Services { get; }

    /// <summary>Repository root and sample-solution paths, discovered once.</summary>
    public TestRepositoryFixtures RepositoryFixtures { get; }

    /// <summary>
    /// Collapses the ~22 duplicate <c>SampleSolution</c> loads that previously caused MSBuild
    /// file-lock contention. One cache per fixture instance, i.e. one per test assembly run.
    /// </summary>
    public WorkspaceIdCache WorkspaceIdCache { get; } = new();

    /// <summary>
    /// Number of times <see cref="DisposeAsync"/> has passed its idempotence guard and entered the
    /// release block. Must never exceed 1 however many callers dispose the fixture. This counts
    /// ENTRIES, not completed releases — the release itself is proven by an observable
    /// post-condition in the regression, since a counter alone would stay green if the release
    /// block were deleted.
    /// </summary>
    internal int DisposalEntryCount => Volatile.Read(ref _ownedResourceDisposalCount);

    public static TestAssemblyFixture Create(ValidationServiceOptions validationOptions) =>
        new(TestServiceContainer.Create(validationOptions), TestRepositoryFixtures.Discover());

    /// <summary>
    /// Returns a real, connected MCP server whose server-owned security options explicitly
    /// sanction repository fixtures and the process temp directory. Direct tool tests use this
    /// instead of relying on a null-server path-validation bypass. Created at most once and
    /// disposed with the fixture.
    /// </summary>
    public async Task<McpServer> GetPathAuthorizedServerAsync()
    {
        Task<McpRootsTestServerFactory.Session> sessionTask;
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            sessionTask = _pathAuthorizedServerTask ??=
                McpRootsTestServerFactory.CreateWithSanctionedRootsAsync(
                    [RepositoryFixtures.RepositoryRootPath, TestTempRoot.Current],
                    CancellationToken.None);
        }

        return (await sessionTask.ConfigureAwait(false)).Server;
    }

    /// <summary>
    /// Loads a workspace from the given solution path, caching the resulting <c>WorkspaceId</c>
    /// so multiple test classes that need the same fixture share a single
    /// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> instance.
    /// </summary>
    public Task<string> GetOrLoadWorkspaceIdAsync(string solutionPath, CancellationToken ct = default) =>
        WorkspaceIdCache.GetOrLoadAsync(Services.WorkspaceManager, solutionPath, ct);

    /// <summary>
    /// Releases everything this fixture owns — the shared <see cref="Services"/>
    /// <c>WorkspaceManager</c>, the workspace-id cache, and the path-authorized server session.
    /// Idempotent: repeat calls are no-ops, so a double-invoked <c>[AssemblyCleanup]</c> cannot
    /// double-dispose. Every step runs even when an earlier one throws.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Task<McpRootsTestServerFactory.Session>? pathAuthorizedServerTask;
        lock (_sessionLock)
        {
            pathAuthorizedServerTask = _pathAuthorizedServerTask;
            _pathAuthorizedServerTask = null;
        }

        Interlocked.Increment(ref _ownedResourceDisposalCount);

        await CleanupFailureCollector.RunAsync(
            "Shared test resource cleanup failed.",
            CleanupFailureCollector.FromAction(WorkspaceIdCache.Clear),
            CleanupFailureCollector.FromAction(Services.WorkspaceManager.Dispose),
            async () =>
            {
                if (pathAuthorizedServerTask is not null)
                {
                    var session = await pathAuthorizedServerTask.ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
    }
}
