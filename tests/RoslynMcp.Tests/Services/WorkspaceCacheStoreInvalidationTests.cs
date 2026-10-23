using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Cache-key invalidation tests for <see cref="WorkspaceCacheStore"/> per the
/// <c>workspace-cache-store-infrastructure</c> plan's validation step (b): "sdk-version
/// bump invalidates". Also covers solution-hash and msbuild-graph-hash bumps and explicit
/// <c>InvalidateAsync</c>.
/// </summary>
/// <remarks>
/// The cache-key triple <c>(solutionHash, sdkVersion, msbuildGraphHash)</c> is the contract.
/// Any single component changing must produce a miss against the prior entry; this is the
/// primary safety net against using a stale cache after a runtime upgrade or a project
/// add/remove.
/// </remarks>
[TestClass]
public sealed class WorkspaceCacheStoreInvalidationTests
{
    /// <summary>
    /// Primary scenario from the plan's risk #1 ("on-disk format becomes a compatibility
    /// surface"): bumping the SDK version component of the key invalidates cleanly. A new
    /// runtime install should never observe a stale graph hash because Roslyn's cache
    /// poisoning here would manifest as confusing build errors.
    /// </summary>
    [TestMethod]
    public async Task SdkVersionBump_InvalidatesPriorEntry()
    {
        await WithStoreAsync(async store =>
        {
            var solutionHash = "sln-stable";
            var graphHash = "graph-stable";
            var beforeBump = new WorkspaceCacheKey(solutionHash, ".NET 10.0.0", graphHash);
            var afterBump = new WorkspaceCacheKey(solutionHash, ".NET 10.0.1", graphHash);
            var entry = SampleEntry(@"C:\repo\Original.csproj");

            Assert.IsTrue(await store.PutAsync(beforeBump, entry, CancellationToken.None));

            // Pre-bump: hit on the original key.
            var preBumpRead = await store.TryGetAsync(beforeBump, CancellationToken.None);
            Assert.IsNotNull(preBumpRead, "Sanity: the original key should hit before bumping the SDK version.");

            // Post-bump: miss on the new key — never observe the prior entry under the new SDK.
            var postBumpRead = await store.TryGetAsync(afterBump, CancellationToken.None);
            Assert.IsNull(postBumpRead, "An SDK-version bump must invalidate the prior cache entry; a stale graph hash post-bump is a runtime poisoning.");
        });
    }

    /// <summary>
    /// A different solution (different .sln/.slnx hash) must produce a miss even when the
    /// other two key components match — the cache is per-solution.
    /// </summary>
    [TestMethod]
    public async Task SolutionHashBump_InvalidatesPriorEntry()
    {
        await WithStoreAsync(async store =>
        {
            var sdk = ".NET 10.0.0";
            var graphHash = "graph-stable";
            var firstSolution = new WorkspaceCacheKey("sln-A", sdk, graphHash);
            var secondSolution = new WorkspaceCacheKey("sln-B", sdk, graphHash);

            await store.PutAsync(firstSolution, SampleEntry(@"C:\A.csproj"), CancellationToken.None);

            var firstSolutionRead = await store.TryGetAsync(firstSolution, CancellationToken.None);
            Assert.IsNotNull(firstSolutionRead);

            var secondSolutionRead = await store.TryGetAsync(secondSolution, CancellationToken.None);
            Assert.IsNull(secondSolutionRead, "A different solution hash must miss even when SDK + graph hashes match.");
        });
    }

    /// <summary>
    /// A graph mutation (project added/removed) bumps the msbuildGraphHash component and
    /// invalidates the prior entry. Without this, a renamed/added project would silently
    /// reuse a stale graph and skip Roslyn's discovery of the new project.
    /// </summary>
    [TestMethod]
    public async Task GraphHashBump_InvalidatesPriorEntry()
    {
        await WithStoreAsync(async store =>
        {
            var solutionHash = "sln";
            var sdk = ".NET 10.0.0";
            var oldGraph = new WorkspaceCacheKey(solutionHash, sdk, "graph-2-projects");
            var newGraph = new WorkspaceCacheKey(solutionHash, sdk, "graph-3-projects");

            await store.PutAsync(oldGraph, SampleEntry(@"C:\Old.csproj"), CancellationToken.None);

            Assert.IsNotNull(await store.TryGetAsync(oldGraph, CancellationToken.None), "Sanity: original graph hash should hit.");
            Assert.IsNull(await store.TryGetAsync(newGraph, CancellationToken.None), "A graph-hash bump (project added/removed) must miss the cache.");
        });
    }

    /// <summary>
    /// Explicit invalidation removes the entry — subsequent reads must miss. Used by
    /// consumers that detect drift independently of the key triple (e.g. an upstream
    /// metadata-reference whose mtime moved).
    /// </summary>
    [TestMethod]
    public async Task InvalidateAsync_RemovesEntry()
    {
        await WithStoreAsync(async store =>
        {
            var key = new WorkspaceCacheKey("sln", ".NET 10.0.0", "graph");
            await store.PutAsync(key, SampleEntry(@"C:\Project.csproj"), CancellationToken.None);

            Assert.IsNotNull(await store.TryGetAsync(key, CancellationToken.None), "Sanity: the entry should be present before invalidation.");

            await store.InvalidateAsync(key, CancellationToken.None);

            Assert.IsNull(await store.TryGetAsync(key, CancellationToken.None), "After InvalidateAsync the key must miss the cache.");
        });
    }

    /// <summary>
    /// Invalidating a missing key is a no-op, not an exception. Consumers may invalidate
    /// pre-emptively without first checking for presence.
    /// </summary>
    [TestMethod]
    public async Task InvalidateAsync_OnMissingKey_IsNoOp()
    {
        await WithStoreAsync(async store =>
        {
            var key = new WorkspaceCacheKey("never-written", ".NET 10.0.0", "graph");

            await store.InvalidateAsync(key, CancellationToken.None);

            // Side-effect: the directory tree may or may not exist depending on whether anything
            // else wrote to this root, but no exception was thrown — that's the whole assertion.
            Assert.IsNull(await store.TryGetAsync(key, CancellationToken.None));
        });
    }

    /// <summary>
    /// Format-version mismatch is treated as a miss (clean upgrade path). We simulate a future
    /// version bump by overwriting the on-disk JSON with a different <c>formatVersion</c>; the
    /// next read should drop the entry rather than crash.
    /// </summary>
    [TestMethod]
    public async Task FormatVersionMismatch_TreatsAsMiss()
    {
        await WithStoreAsync(async store =>
        {
            var key = new WorkspaceCacheKey("sln", ".NET 10.0.0", "graph");
            await store.PutAsync(key, SampleEntry(@"C:\Project.csproj"), CancellationToken.None);

            // Locate the on-disk entry by asking the store, then rewrite it with a fictional
            // future format version.
            var entryPath = store.ResolveEntryPath(key);
            Assert.IsTrue(File.Exists(entryPath), "The store must produce a discoverable entry path for an existing entry.");

            const string futureVersionJson = """{"formatVersion":99,"projectGraph":[],"metadataReferences":[],"capturedAtUtc":"2026-05-05T00:00:00Z"}""";
            await File.WriteAllTextAsync(entryPath, futureVersionJson);

            var read = await store.TryGetAsync(key, CancellationToken.None);

            Assert.IsNull(read, "A format-version mismatch must read as a miss so the cache can be re-populated under the current schema.");
        });
    }

    /// <summary>
    /// Corrupt JSON is treated as a miss — never crashes the load path. This is the contract
    /// against partial-write or external-corruption scenarios where the next read sees an
    /// unparseable file.
    /// </summary>
    [TestMethod]
    public async Task CorruptJson_TreatsAsMiss()
    {
        await WithStoreAsync(async store =>
        {
            var key = new WorkspaceCacheKey("sln", ".NET 10.0.0", "graph");
            await store.PutAsync(key, SampleEntry(@"C:\Project.csproj"), CancellationToken.None);

            var entryPath = store.ResolveEntryPath(key);
            await File.WriteAllTextAsync(entryPath, "this is not valid json {{{");

            var read = await store.TryGetAsync(key, CancellationToken.None);

            Assert.IsNull(read, "Corrupt JSON must read as a miss; the cache must never propagate a parse failure to the consumer.");
        });
    }


    private static async Task WithStoreAsync(Func<WorkspaceCacheStore, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, "WorkspaceCacheStoreInvalidation", Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var store = new WorkspaceCacheStore(root);
            await body(store);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Cache invalidation fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static WorkspaceCacheEntry SampleEntry(string projectPath) =>
        new(
            ProjectGraph: new[] { new CachedProjectGraphNode(projectPath, Array.Empty<string>()) },
            MetadataReferences: new[]
            {
                new CachedProjectMetadataReferences(
                    projectPath,
                    Array.Empty<CachedMetadataReference>()),
            },
            CapturedAtUtc: DateTime.UtcNow);
}
