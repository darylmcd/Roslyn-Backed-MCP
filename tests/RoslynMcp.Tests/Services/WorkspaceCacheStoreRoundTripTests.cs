using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Direct unit tests for <see cref="WorkspaceCacheStore"/> covering the write→read→equality
/// round-trip per the <c>workspace-cache-store-infrastructure</c> plan's validation step (b).
/// </summary>
/// <remarks>
/// Each test creates an isolated cache root under <see cref="Path.GetTempPath"/> so concurrent
/// test runs don't poison each other and the user's real <c>~/.roslyn-mcp/cache/</c> is never
/// touched.
/// </remarks>
[TestClass]
public sealed class WorkspaceCacheStoreRoundTripTests
{
    private string _cacheRoot = null!;
    private WorkspaceCacheStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheRoot = Path.Combine(TestTempRoot.Current, "WorkspaceCacheStoreRoundTrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheRoot);
        _store = new WorkspaceCacheStore(_cacheRoot);
    }

    [TestCleanup]
    public void Teardown()
    {
        try
        {
            if (Directory.Exists(_cacheRoot))
            {
                Directory.Delete(_cacheRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup — another test runner instance may hold a lock.
        }
    }

    /// <summary>
    /// Happy path: write an entry, read it back, assert structural equality on every field.
    /// This is the contract every consumer of the cache relies on; if a serialization shape
    /// drifts, this test breaks first.
    /// </summary>
    [TestMethod]
    public async Task PutThenGet_ReturnsStructurallyEqualEntry()
    {
        var key = new WorkspaceCacheKey("sln-hash-1", ".NET 10.0.0", "graph-hash-1");
        var capturedAt = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var entry = new WorkspaceCacheEntry(
            ProjectGraph: new[]
            {
                new CachedProjectGraphNode(
                    @"C:\repo\Project1\Project1.csproj",
                    new[] { @"C:\repo\Project2\Project2.csproj" }),
                new CachedProjectGraphNode(
                    @"C:\repo\Project2\Project2.csproj",
                    Array.Empty<string>()),
            },
            MetadataReferences: new[]
            {
                new CachedProjectMetadataReferences(
                    @"C:\repo\Project1\Project1.csproj",
                    new[]
                    {
                        new CachedMetadataReference(@"C:\nuget\System.Text.Json.dll", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                        new CachedMetadataReference(@"C:\repo\Project2\bin\Debug\net10.0\Project2.dll", new DateTime(2026, 5, 5, 11, 30, 0, DateTimeKind.Utc)),
                    }),
            },
            CapturedAtUtc: capturedAt);

        var putResult = await _store.PutAsync(key, entry, CancellationToken.None);
        Assert.IsTrue(putResult, "PutAsync should succeed against a writable temp directory.");

        var roundTripped = await _store.TryGetAsync(key, CancellationToken.None);

        Assert.IsNotNull(roundTripped, "TryGetAsync should return the entry just written under the same key.");
        Assert.AreEqual(2, roundTripped!.ProjectGraph.Count, "ProjectGraph node count should round-trip.");
        Assert.AreEqual(@"C:\repo\Project1\Project1.csproj", roundTripped.ProjectGraph[0].ProjectPath);
        Assert.AreEqual(1, roundTripped.ProjectGraph[0].ProjectReferences.Count);
        Assert.AreEqual(@"C:\repo\Project2\Project2.csproj", roundTripped.ProjectGraph[0].ProjectReferences[0]);
        Assert.AreEqual(0, roundTripped.ProjectGraph[1].ProjectReferences.Count, "Empty project-references list should round-trip as empty, not null.");

        Assert.AreEqual(1, roundTripped.MetadataReferences.Count);
        var refList = roundTripped.MetadataReferences[0];
        Assert.AreEqual(@"C:\repo\Project1\Project1.csproj", refList.ProjectPath);
        Assert.AreEqual(2, refList.References.Count);
        Assert.AreEqual(@"C:\nuget\System.Text.Json.dll", refList.References[0].AssemblyPath);
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), refList.References[0].LastWriteTimeUtc, "LastWriteTimeUtc must round-trip exactly to detect upstream-build invalidation.");
        Assert.AreEqual(@"C:\repo\Project2\bin\Debug\net10.0\Project2.dll", refList.References[1].AssemblyPath);
        Assert.AreEqual(new DateTime(2026, 5, 5, 11, 30, 0, DateTimeKind.Utc), refList.References[1].LastWriteTimeUtc);

        Assert.AreEqual(capturedAt, roundTripped.CapturedAtUtc, "CapturedAtUtc must round-trip exactly.");
    }

    /// <summary>
    /// A miss returns null without throwing — the contract for cold-cache behavior. The
    /// consumer (WorkspaceManager, future row) relies on this to fall through to the existing
    /// cold-load path without exception handling.
    /// </summary>
    [TestMethod]
    public async Task TryGet_OnMissingKey_ReturnsNull()
    {
        var key = new WorkspaceCacheKey("never-written", ".NET 10.0.0", "graph-hash");

        var result = await _store.TryGetAsync(key, CancellationToken.None);

        Assert.IsNull(result, "TryGetAsync against a missing key must return null, not throw.");
    }

    /// <summary>
    /// Re-writing under the same key replaces the prior entry. Idempotency is required for
    /// the prewarm-on-load consumer scenario where a fresh load may capture slightly different
    /// metadata-reference timestamps even within the same MSBuild graph.
    /// </summary>
    [TestMethod]
    public async Task PutTwice_OverwritesPriorEntry()
    {
        var key = new WorkspaceCacheKey("sln-hash-overwrite", ".NET 10.0.0", "graph-hash");
        var firstEntry = new WorkspaceCacheEntry(
            new[] { new CachedProjectGraphNode(@"C:\repo\First.csproj", Array.Empty<string>()) },
            new[] { new CachedProjectMetadataReferences(@"C:\repo\First.csproj", Array.Empty<CachedMetadataReference>()) },
            new DateTime(2026, 5, 5, 10, 0, 0, DateTimeKind.Utc));
        var secondEntry = new WorkspaceCacheEntry(
            new[] { new CachedProjectGraphNode(@"C:\repo\Second.csproj", Array.Empty<string>()) },
            new[] { new CachedProjectMetadataReferences(@"C:\repo\Second.csproj", Array.Empty<CachedMetadataReference>()) },
            new DateTime(2026, 5, 5, 11, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(await _store.PutAsync(key, firstEntry, CancellationToken.None));
        Assert.IsTrue(await _store.PutAsync(key, secondEntry, CancellationToken.None));

        var roundTripped = await _store.TryGetAsync(key, CancellationToken.None);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(@"C:\repo\Second.csproj", roundTripped!.ProjectGraph[0].ProjectPath, "Second put should win — the cache must always reflect the latest write.");
        Assert.AreEqual(new DateTime(2026, 5, 5, 11, 0, 0, DateTimeKind.Utc), roundTripped.CapturedAtUtc);
    }

    /// <summary>
    /// Different cache keys yield independent on-disk locations — a round-trip under one key
    /// must not leak into reads under a different key. This is the cross-key isolation the
    /// path layout (<c>solution-hash/sdk-version/msbuild-graph-hash/</c>) provides.
    /// </summary>
    [TestMethod]
    public async Task DifferentKeys_StoreIndependently()
    {
        var keyA = new WorkspaceCacheKey("sln-A", ".NET 10.0.0", "graph-1");
        var keyB = new WorkspaceCacheKey("sln-B", ".NET 10.0.0", "graph-1");
        var entryA = new WorkspaceCacheEntry(
            new[] { new CachedProjectGraphNode(@"C:\A.csproj", Array.Empty<string>()) },
            Array.Empty<CachedProjectMetadataReferences>(),
            DateTime.UtcNow);
        var entryB = new WorkspaceCacheEntry(
            new[] { new CachedProjectGraphNode(@"C:\B.csproj", Array.Empty<string>()) },
            Array.Empty<CachedProjectMetadataReferences>(),
            DateTime.UtcNow);

        await _store.PutAsync(keyA, entryA, CancellationToken.None);
        await _store.PutAsync(keyB, entryB, CancellationToken.None);

        var readA = await _store.TryGetAsync(keyA, CancellationToken.None);
        var readB = await _store.TryGetAsync(keyB, CancellationToken.None);

        Assert.IsNotNull(readA);
        Assert.IsNotNull(readB);
        Assert.AreEqual(@"C:\A.csproj", readA!.ProjectGraph[0].ProjectPath, "Key A must read its own entry, not B's.");
        Assert.AreEqual(@"C:\B.csproj", readB!.ProjectGraph[0].ProjectPath, "Key B must read its own entry, not A's.");
    }

    /// <summary>
    /// A null project-references list deserializes as an empty (non-null) list. C# nullable
    /// reference types let the input vector through, but the consumer contract treats every
    /// list as non-null — confirm the JSON layer normalizes correctly.
    /// </summary>
    [TestMethod]
    public async Task EmptyCollections_RoundTripAsEmpty_NotNull()
    {
        var key = new WorkspaceCacheKey("empty-collections", ".NET 10.0.0", "graph");
        var entry = new WorkspaceCacheEntry(
            ProjectGraph: Array.Empty<CachedProjectGraphNode>(),
            MetadataReferences: Array.Empty<CachedProjectMetadataReferences>(),
            CapturedAtUtc: DateTime.UtcNow);

        await _store.PutAsync(key, entry, CancellationToken.None);
        var roundTripped = await _store.TryGetAsync(key, CancellationToken.None);

        Assert.IsNotNull(roundTripped);
        Assert.IsNotNull(roundTripped!.ProjectGraph, "ProjectGraph must round-trip as non-null when empty.");
        Assert.AreEqual(0, roundTripped.ProjectGraph.Count);
        Assert.IsNotNull(roundTripped.MetadataReferences);
        Assert.AreEqual(0, roundTripped.MetadataReferences.Count);
    }
}
