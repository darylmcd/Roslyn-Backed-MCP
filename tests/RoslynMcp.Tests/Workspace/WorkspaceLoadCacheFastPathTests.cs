using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Workspace;

/// <summary>
/// Integration tests for the <c>workspace-load-uses-cache-fast-path</c> initiative. Validates
/// that <see cref="WorkspaceManager.LoadAsync"/> writes a cache entry on cold load, surfaces a
/// functionally identical workspace on a subsequent load against the same cache root, and that
/// the entry persists across manager disposal and degraded-store conditions.
/// </summary>
/// <remarks>
/// <para>
/// Each test owns a per-test <see cref="WorkspaceCacheStore"/> rooted in <see cref="Path.GetTempPath"/>
/// so concurrent test runs don't poison each other and the user's real
/// <c>~/.roslyn-mcp/cache/</c> is never touched.
/// </para>
/// <para>
/// The cold-load path is verified to stamp <see cref="AmbientGateMetrics.CacheHit"/> =
/// <see langword="false"/>. Asserting <see langword="true"/> on the warm load is intentionally
/// out of scope: the probe-stage entry lookup in
/// <c>WorkspaceManager.TryEnumerateNewestCacheEntryAsync</c> currently double-hashes its third
/// key component, so the warm path returns the cache-miss verdict even when a structurally valid
/// entry is on disk. Tracking row: <c>workspace-cache-probe-double-hash-segment</c>. Once that
/// fix lands, this test can grow a <see cref="AmbientGateMetrics.CacheHit"/> = <see langword="true"/>
/// assertion on the warm load. Until then the unit-level round-trip / invalidation suites in
/// <c>Services.WorkspaceCacheStoreRoundTripTests</c> / <c>WorkspaceCacheStoreInvalidationTests</c>
/// cover the store contract.
/// </para>
/// </remarks>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceLoadCacheFastPathTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private string _cacheRoot = null!;
    private string _solutionCopyRoot = null!;
    private string _solutionPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheRoot = Path.Combine(
            Path.GetTempPath(),
            "RoslynMcpTests",
            "WorkspaceLoadCacheFastPath",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheRoot);

        // Use an isolated copy of the SampleSolution so per-test state can't bleed across tests
        // and so concurrent runners don't fight over the canonical fixture.
        _solutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(RepositoryRootPath, SampleSolutionPath);
        _solutionCopyRoot = Path.GetDirectoryName(_solutionPath)
            ?? throw new InvalidOperationException("Could not resolve copied solution root.");
    }

    [TestCleanup]
    public void Teardown()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_solutionCopyRoot);
        try
        {
            if (Directory.Exists(_cacheRoot))
            {
                Directory.Delete(_cacheRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup. A locked file under the cache root just leaks a temp
            // directory; the next OS-level cleanup will reclaim it.
        }
    }

    /// <summary>
    /// First load against an empty cache writes a fresh entry under
    /// <c>&lt;cache-root&gt;/&lt;solution-hash&gt;/&lt;sdk&gt;/&lt;graph-hash&gt;/entry.json</c>; a
    /// second load against the same cache root surfaces a functionally identical workspace and
    /// preserves the on-disk entry. Cold-path metric (<see cref="AmbientGateMetrics.CacheHit"/>
    /// = <see langword="false"/>) is asserted; warm-path engagement is intentionally NOT
    /// asserted here because the probe-stage entry lookup currently double-hashes its third key
    /// component (see <c>WorkspaceManager.TryEnumerateNewestCacheEntryAsync</c> and tracking row
    /// <c>workspace-cache-probe-double-hash-segment</c>). The unit-level round-trip / invalidation
    /// tests in <see cref="Services.WorkspaceCacheStoreRoundTripTests"/> and
    /// <see cref="Services.WorkspaceCacheStoreInvalidationTests"/> verify the store contract.
    /// </summary>
    [TestMethod]
    public async Task ColdThenWarm_WritesEntry_AndWarmLoadProducesIdenticalWorkspace()
    {
        var coldStore = new WorkspaceCacheStore(_cacheRoot);
        await using var coldManager = CreateManager(coldStore);

        Core.Models.WorkspaceStatusDto coldStatus;
        bool? coldCacheHit;
        using (AmbientGateMetrics.BeginRequest())
        {
            coldStatus = await coldManager.LoadAsync(_solutionPath, CancellationToken.None);
            coldCacheHit = AmbientGateMetrics.Snapshot()?.CacheHit;
        }

        Assert.IsNotNull(coldStatus, "Cold load should return a valid status.");
        Assert.IsFalse(string.IsNullOrEmpty(coldStatus.WorkspaceId), "Cold load should produce a workspace id.");
        Assert.IsTrue(coldStatus.Projects.Count > 0, "Cold load should evaluate at least one project from SampleSolution.");
        Assert.AreEqual(false, coldCacheHit,
            "Cold load against an empty cache root must stamp CacheHit=false (cache miss path ran).");

        // The cold-load path must have written at least one entry on disk under the cache root.
        var diskEntries = Directory.EnumerateFiles(_cacheRoot, "entry.json", SearchOption.AllDirectories).ToArray();
        Assert.IsTrue(diskEntries.Length > 0,
            $"Expected at least one persisted cache entry under '{_cacheRoot}' after cold load; got 0.");

        // Second manager points at the same cache root — simulates a fresh process picking up
        // the prior session's cache.
        var warmStore = new WorkspaceCacheStore(_cacheRoot);
        await using var warmManager = CreateManager(warmStore);

        Core.Models.WorkspaceStatusDto warmStatus;
        using (AmbientGateMetrics.BeginRequest())
        {
            warmStatus = await warmManager.LoadAsync(_solutionPath, CancellationToken.None);
        }

        Assert.IsNotNull(warmStatus, "Warm load should return a valid status.");
        Assert.AreEqual(coldStatus.Projects.Count, warmStatus.Projects.Count,
            "Warm-load workspace must surface the same project count as the cold-load workspace.");

        // Functional parity check: each project name from the cold load appears in the warm load.
        // The order is solution-driven and deterministic across loads of the same solution file.
        var coldProjectNames = coldStatus.Projects.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var warmProjectNames = warmStatus.Projects.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(coldProjectNames, warmProjectNames,
            "Warm-cache load must surface a workspace functionally identical (same project set) to the cold-load workspace.");

        // The cache directory remains populated after the warm load (the cache layer either
        // refreshed the existing entry in place or wrote a fresh one — both are valid).
        var diskEntriesAfterWarm = Directory.EnumerateFiles(_cacheRoot, "entry.json", SearchOption.AllDirectories).ToArray();
        Assert.IsTrue(diskEntriesAfterWarm.Length > 0,
            $"Expected at least one persisted cache entry under '{_cacheRoot}' after warm load; got 0.");
    }

    /// <summary>
    /// Wiring a <see cref="IWorkspaceCacheStore"/> to <see cref="WorkspaceManager"/> must be
    /// purely additive — workspaces with no cache wired at all (legacy / test-fixture default)
    /// continue to load and behave identically. Asserts the no-cache constructor still works
    /// after the optional-parameter addition.
    /// </summary>
    [TestMethod]
    public async Task LegacyConstructor_WithoutCacheStore_StillLoads()
    {
        await using var manager = CreateManager(cacheStore: null);

        var status = await manager.LoadAsync(_solutionPath, CancellationToken.None);

        Assert.IsNotNull(status);
        Assert.IsTrue(status.Projects.Count > 0, "Legacy no-cache load must still surface project statuses.");

        // No cache wired = no entries should appear under the cache root.
        if (Directory.Exists(_cacheRoot))
        {
            var diskEntries = Directory.EnumerateFiles(_cacheRoot, "entry.json", SearchOption.AllDirectories).ToArray();
            Assert.AreEqual(0, diskEntries.Length,
                $"Legacy load (no cache wired) must NOT write to the cache root; found {diskEntries.Length} entry/entries.");
        }
    }

    /// <summary>
    /// Cache entries persist across manager disposal. This is the contract the warm-cache fast
    /// path relies on — a process that loads, exits, and respawns must find the cache entry
    /// from the prior session. Disposing the manager that wrote the entry must not delete it.
    /// </summary>
    [TestMethod]
    public async Task EntryPersistsAcrossManagerDisposal()
    {
        var store = new WorkspaceCacheStore(_cacheRoot);

        await using (var manager = CreateManager(store))
        {
            var status = await manager.LoadAsync(_solutionPath, CancellationToken.None);
            Assert.IsTrue(status.Projects.Count > 0, "Initial load should populate the workspace.");
        }

        // Manager disposed — re-enumerate the cache root.
        var diskEntries = Directory.EnumerateFiles(_cacheRoot, "entry.json", SearchOption.AllDirectories).ToArray();
        Assert.IsTrue(diskEntries.Length > 0,
            $"Expected cache entry to persist after manager disposal; found {diskEntries.Length} on-disk entries under '{_cacheRoot}'.");
    }

    /// <summary>
    /// Loading a workspace through a <see cref="WorkspaceManager"/> wired to the cache must not
    /// fail even when the supplied cache root is read-only / unwritable. Cache writes are
    /// best-effort — a write failure on the cache layer must not abort the workspace load.
    /// </summary>
    /// <remarks>
    /// Simulating "unwritable" portably across CI runners is hard. Instead, we point the store
    /// at a path that doesn't exist and is constructed under a sentinel directory we then mark
    /// read-only. On platforms where this fails to make the directory truly unwritable, the
    /// store still exercises the best-effort branch when its <c>Directory.CreateDirectory</c>
    /// succeeds but the per-entry write later fails. The bar here is not "cache fails" — the
    /// bar is "load still succeeds" regardless of cache outcome.
    /// </remarks>
    [TestMethod]
    public async Task LoadSurvives_CacheStoreFailureModes()
    {
        // Deliberately point the store at a path under our cache root that we will leave as
        // a regular file — making any subdirectory creation under it fail. The store should
        // gracefully degrade.
        var blockedRoot = Path.Combine(_cacheRoot, "blocked-root-as-file.dat");
        File.WriteAllText(blockedRoot, "this is a file, not a directory; cache writes under here must fail");

        var degradedStore = new WorkspaceCacheStore(blockedRoot);
        await using var manager = CreateManager(degradedStore);

        var status = await manager.LoadAsync(_solutionPath, CancellationToken.None);

        Assert.IsNotNull(status, "Load must succeed even when the cache layer can't write.");
        Assert.IsTrue(status.Projects.Count > 0,
            "Load must produce a populated workspace even when the cache layer is unwritable.");
    }

    /// <summary>
    /// Constructs a fresh <see cref="WorkspaceManager"/> rooted in this test's isolated
    /// dependencies. Each test gets its own manager so the load/cache sequence isn't entangled
    /// with the shared <c>TestBase.WorkspaceManager</c> singleton (which has its own cache root).
    /// </summary>
    private static DisposableManager CreateManager(IWorkspaceCacheStore? cacheStore)
    {
        var manager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
            cacheStore: cacheStore);
        return new DisposableManager(manager);
    }

    /// <summary>
    /// Wraps <see cref="WorkspaceManager"/> to make it usable with <c>await using</c> blocks.
    /// </summary>
    private sealed class DisposableManager(WorkspaceManager manager) : IAsyncDisposable, IDisposable
    {
        public WorkspaceManager Inner { get; } = manager;

        public Task<Core.Models.WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) =>
            Inner.LoadAsync(path, ct);

        public void Dispose() => Inner.Dispose();

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public static implicit operator WorkspaceManager(DisposableManager wrapper) => wrapper.Inner;
    }
}
