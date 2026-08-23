using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class MutationAnalysisSideEffectsTests : SharedWorkspaceTestBase
{
    private static string CopiedRoot { get; set; } = null!;
    private static string CopiedSolutionPath { get; set; } = null!;
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        CopiedSolutionPath = CreateSampleSolutionCopy();
        CopiedRoot = Path.GetDirectoryName(CopiedSolutionPath)!;

        // Fixture #1: a file-IO-only class (no instance-field reassignment) — the canonical
        // case from the 2026-04-07 FirewallAnalyzer audit. Pre-fix find_type_mutations
        // returned zero mutating members because IsMutatingMember only checked
        // IAssignmentOperation on instance fields. After the side-effect classifier lands,
        // every public method that calls File.WriteAllText/Delete/etc. is reported with
        // MutationScopes containing IO.
        var ioPath = Path.Combine(CopiedRoot, "SampleLib", "FileSnapshotStoreSample.cs");
        await File.WriteAllTextAsync(ioPath, """
namespace SampleLib;

using System.IO;

public class FileSnapshotStoreSample
{
    private readonly string _root;

    public FileSnapshotStoreSample(string root)
    {
        _root = root;
    }

    public void WriteManifest(string content)
    {
        File.WriteAllText(Path.Combine(_root, "manifest.txt"), content);
    }

    public void DeleteManifest()
    {
        File.Delete(Path.Combine(_root, "manifest.txt"));
    }

    public string ReadManifest()
    {
        return File.ReadAllText(Path.Combine(_root, "manifest.txt"));
    }
}

public static class FileSnapshotStoreSampleCaller
{
    public static void Persist(FileSnapshotStoreSample store)
    {
        store.WriteManifest("content");
    }
}
""", CancellationToken.None);

        // Fixture #3: a lifecycle-manager class that mutates a ConcurrentDictionary field via
        // TryAdd / TryRemove / indexer-write / Clear across sync + async methods. Regression
        // guard for find-type-mutations-undercounts-lifecycle-writes — pre-fix only the Clear()
        // call in DisposeAll was detected because TryAdd / TryRemove were not in the mutator
        // name list and indexer-writes were not recognized as collection mutations.
        var lifecyclePath = Path.Combine(CopiedRoot, "SampleLib", "LifecycleManagerSample.cs");
        await File.WriteAllTextAsync(lifecyclePath, """
namespace SampleLib;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public class LifecycleManagerSample
{
    private readonly ConcurrentDictionary<string, string> _sessions = new();

    public async Task<string> LoadAsync(string path, CancellationToken ct)
    {
        await Task.Yield();
        var id = System.Guid.NewGuid().ToString();
        _sessions.TryAdd(id, path);
        return id;
    }

    public async Task ReloadAsync(string id, CancellationToken ct)
    {
        await Task.Yield();
        _sessions[id] = "reloaded";
    }

    public bool Close(string id)
    {
        return _sessions.TryRemove(id, out _);
    }

    public void DisposeAll()
    {
        _sessions.Clear();
    }
}
""", CancellationToken.None);

        // Fixture #2: a no-side-effect computational class — should report zero mutations.
        var pureClassPath = Path.Combine(CopiedRoot, "SampleLib", "PureComputeSample.cs");
        await File.WriteAllTextAsync(pureClassPath, """
namespace SampleLib;

public class PureComputeSample
{
    private readonly int _multiplier;

    public PureComputeSample(int multiplier)
    {
        _multiplier = multiplier;
    }

    public int Compute(int input)
    {
        return input * _multiplier;
    }
}
""", CancellationToken.None);

        // Fixture #4: a compound-mutation class whose SyncEntryAsync performs BOTH a
        // ConcurrentDictionary.TryAdd (CollectionWrite) AND a File.WriteAllText (IO).
        // Pre-fix ClassifyMethodMutationScope short-circuited on the side-effect scope and
        // returned only "IO", masking the CollectionWrite signal. Regression guard for
        // gh #741 — MutationScopes must surface every applicable scope rather than only
        // the highest-severity one.
        var compoundPath = Path.Combine(CopiedRoot, "SampleLib", "CompoundMutationSample.cs");
        await File.WriteAllTextAsync(compoundPath, """
namespace SampleLib;

using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class CompoundMutationSample
{
    private readonly ConcurrentDictionary<string, string> _entries = new();

    public async Task SyncEntryAsync(string path, string content, CancellationToken ct)
    {
        await Task.Yield();
        File.WriteAllText(path, content);
        _entries.TryAdd(path, content);
    }
}
""", CancellationToken.None);

        var status = await WorkspaceManager.LoadAsync(CopiedSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        try
        {
            if (WorkspaceId is not null)
            {
                WorkspaceManager.Close(WorkspaceId);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(CopiedRoot);
            DisposeServices();
        }
    }

    [TestMethod]
    public async Task FindTypeMutations_FileIOClass_FlagsWriteMethodsAsIO()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.FileSnapshotStoreSample");
        var result = await MutationAnalysisService.FindTypeMutationsAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result, "FileSnapshotStoreSample should resolve to a named type.");

        var writeManifest = result.MutatingMembers.FirstOrDefault(m => m.Name == "WriteManifest");
        Assert.IsNotNull(writeManifest, "WriteManifest should be flagged as a mutating member after the side-effect classifier lands.");
        CollectionAssert.Contains(writeManifest.MutationScopes.ToList(), SideEffectClassifier.Scopes.IO,
            "File.WriteAllText should classify as IO.");
        var externalCaller = writeManifest.ExternalCallers.Single(
            caller => caller.ContainingMember?.Contains("FileSnapshotStoreSampleCaller.Persist", StringComparison.Ordinal) == true);
        Assert.IsNotNull(externalCaller.Location);
        Assert.AreEqual(externalCaller.FilePath, externalCaller.Location.FilePath);
        Assert.AreEqual(externalCaller.StartLine, externalCaller.Location.StartLine);
        Assert.AreEqual(externalCaller.StartColumn, externalCaller.Location.StartColumn);
        Assert.AreEqual(externalCaller.CallerPhase, externalCaller.Location.Classification);

        var deleteManifest = result.MutatingMembers.FirstOrDefault(m => m.Name == "DeleteManifest");
        Assert.IsNotNull(deleteManifest);
        CollectionAssert.Contains(deleteManifest.MutationScopes.ToList(), SideEffectClassifier.Scopes.IO,
            "File.Delete should classify as IO.");
    }

    [TestMethod]
    public async Task FindTypeMutations_CompoundIOAndCollectionWrite_ReportsBothScopes()
    {
        // Regression guard for gh #741: ClassifyMethodMutationScope pre-fix returned at the
        // first non-null result from TryClassifyMethodSideEffects, so a method performing
        // BOTH IO (File.WriteAllText) and CollectionWrite (ConcurrentDictionary.TryAdd)
        // reported only "IO". The single-scope `MutationScope` string field has been
        // replaced by `MutationScopes` (IReadOnlyList<string>) which must surface every
        // applicable scope.
        var locator = SymbolLocator.ByMetadataName("SampleLib.CompoundMutationSample");
        var result = await MutationAnalysisService.FindTypeMutationsAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result, "CompoundMutationSample should resolve to a named type.");

        var syncEntry = result.MutatingMembers.FirstOrDefault(m => m.Name == "SyncEntryAsync");
        Assert.IsNotNull(syncEntry, "SyncEntryAsync should be flagged as a mutating member (File.WriteAllText + TryAdd).");

        var scopes = syncEntry.MutationScopes.ToList();
        CollectionAssert.Contains(scopes, SideEffectClassifier.Scopes.IO,
            $"SyncEntryAsync should report IO for File.WriteAllText. Got: [{string.Join(", ", scopes)}]");
        CollectionAssert.Contains(scopes, SideEffectClassifier.Scopes.CollectionWrite,
            $"SyncEntryAsync should report CollectionWrite for _entries.TryAdd. Got: [{string.Join(", ", scopes)}]");
    }

    [TestMethod]
    public async Task FindTypeMutations_LifecycleManager_FlagsAsyncCollectionMutators()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.LifecycleManagerSample");
        var result = await MutationAnalysisService.FindTypeMutationsAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result, "LifecycleManagerSample should resolve to a named type.");

        // Regression: at least LoadAsync (TryAdd), ReloadAsync (indexer-write), Close (TryRemove),
        // DisposeAll (Clear) — four mutating members. Pre-fix only DisposeAll was detected because
        // TryAdd/TryRemove were missing from the collection-mutator name list and indexer-writes
        // were not recognized as collection mutations.
        var mutatorNames = result.MutatingMembers.Select(m => m.Name).ToHashSet();
        Assert.IsTrue(mutatorNames.Contains("LoadAsync"),
            $"LoadAsync (TryAdd in async body) should be flagged. Got: [{string.Join(", ", mutatorNames)}]");
        Assert.IsTrue(mutatorNames.Contains("ReloadAsync"),
            $"ReloadAsync (indexer-write in async body) should be flagged. Got: [{string.Join(", ", mutatorNames)}]");
        Assert.IsTrue(mutatorNames.Contains("Close"),
            $"Close (TryRemove) should be flagged. Got: [{string.Join(", ", mutatorNames)}]");
        Assert.IsTrue(mutatorNames.Contains("DisposeAll"),
            $"DisposeAll (Clear) should be flagged. Got: [{string.Join(", ", mutatorNames)}]");
        Assert.IsTrue(result.MutatingMembers.Count >= 4,
            $"Expected at least 4 mutating members on LifecycleManagerSample, got {result.MutatingMembers.Count}.");
    }

    [TestMethod]
    public async Task FindTypeMutations_PureComputeClass_ReportsZeroMutations()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.PureComputeSample");
        var result = await MutationAnalysisService.FindTypeMutationsAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.MutatingMembers.Count,
            "PureComputeSample has no settable properties, no field writes, and no side-effect calls — should report zero mutations.");
    }
}
