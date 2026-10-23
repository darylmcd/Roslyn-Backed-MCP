using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceCacheMalformedShapeTests
{
    [TestMethod]
    [DataRow(false, """{"formatVersion":1}""")]
    [DataRow(true, """{"formatVersion":1}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[null],"metadataReferences":[]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[null],"metadataReferences":[]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[{"projectPath":"owned.csproj","projectReferences":null}],"metadataReferences":[]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[{"projectPath":"owned.csproj","projectReferences":null}],"metadataReferences":[]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[{"projectPath":null,"projectReferences":[]}],"metadataReferences":[]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[{"projectPath":null,"projectReferences":[]}],"metadataReferences":[]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[null]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[null]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":null}]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":null}]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":[null]}]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":[null]}]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":[{"assemblyPath":null}]}]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":[{"assemblyPath":null}]}]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[{"projectPath":"owned.csproj","projectReferences":[null]}],"metadataReferences":[]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[{"projectPath":"owned.csproj","projectReferences":[null]}],"metadataReferences":[]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":null,"references":[]}]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":null,"references":[]}]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[{"projectPath":" ","projectReferences":[]}],"metadataReferences":[]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[{"projectPath":" ","projectReferences":[]}],"metadataReferences":[]}""")]
    [DataRow(false, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":[{"assemblyPath":" "}]}]}""")]
    [DataRow(true, """{"formatVersion":1,"projectGraph":[],"metadataReferences":[{"projectPath":"owned.csproj","references":[{"assemblyPath":" "}]}]}""")]
    public async Task SyntacticallyValidMalformedDto_IsCorruptCacheMiss(bool discovered, string json)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(WorkspaceCacheMalformedShapeTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var entry = Path.Combine(root, "Entry.slnx");
            await File.WriteAllTextAsync(entry, "<Solution />");
            var store = new WorkspaceCacheStore(Path.Combine(root, "cache"));
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) =>
                new OwnedTestWorkspacePathPolicy([root]).ValidateAsync("malformed-cache", paths, ct);
            var hash = await WorkspaceCacheCoordinator.ComputeSolutionContentHashAsync(entry, Authorize, CancellationToken.None);
            Assert.IsNotNull(hash);
            var key = new WorkspaceCacheKey(hash, WorkspaceCacheCoordinator.ResolveSdkVersionForCacheKey(), "actual-graph");
            var persisted = store.ResolveEntryPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(persisted)!);
            await File.WriteAllTextAsync(persisted, json);
            if (discovered)
            {
                var probe = await new WorkspaceCacheCoordinator(store, NullLogger.Instance).TryProbeAsync(entry, Authorize, CancellationToken.None);
                Assert.IsNotNull(probe, "A malformed owned cache entry cannot suppress the authorized native-load candidate stage.");
                Assert.IsNull(probe.CachedEntry);
            }
            else Assert.IsNull(await store.TryGetAsync(key, CancellationToken.None));
            Assert.IsFalse(File.Exists(persisted), "Malformed persisted shape must be removed through the same corrupt-entry path as invalid JSON.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Malformed cache fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
