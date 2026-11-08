using System.Text.Json.Nodes;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// composite-redemption-admission-boundary: <see cref="ICompositePreviewStore.PeekChangedPaths"/>
/// is the write-set peek the dispatch layer revalidates against the sanctioned roots before it lets
/// a redemption claim the token. It must describe what <c>Retrieve</c> will hand out, and "unknown"
/// must be <see langword="null"/> so the caller can fail closed.
/// </summary>
[TestClass]
public sealed class CompositePreviewStoreTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup() =>
        _root = Path.Combine(TestTempRoot.Current, "CompositePreviewStore", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Teardown() => TestFixtureFileSystem.DeleteDirectoryIfExists(_root);

    [TestMethod]
    public void PeekChangedPaths_InMemory_ReturnsEveryMutationPathWithoutConsuming()
    {
        var store = new CompositePreviewStore();
        var token = store.Store(
            "ws-1",
            1,
            "two files",
            [new CompositeFileMutation(@"C:\proj\A.cs", "// a"), new CompositeFileMutation(@"C:\proj\B.cs", null, DeleteFile: true)]);

        CollectionAssert.AreEqual(new[] { @"C:\proj\A.cs", @"C:\proj\B.cs" }, store.PeekChangedPaths(token)!.ToArray());
        Assert.IsNotNull(store.Retrieve(token, null), "The peek must not consume the entry.");
        Assert.IsNull(store.PeekChangedPaths(Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public void PeekChangedPaths_Persistent_ReadsThePersistedRecordNotTheCreatorsMemory()
    {
        var store = CreatePersistentStore();
        var token = store.Store("ws-1", 1, "tampered", [new CompositeFileMutation(@"C:\proj\Original.cs", "// a")]);
        var payloadPath = PayloadPath(token);
        var payload = JsonNode.Parse(File.ReadAllText(payloadPath))!;
        payload["Mutations"]![0]!["FilePath"] = @"C:\elsewhere\Tampered.cs";
        // A consistent forgery: the fingerprint follows the mutation (the target does not exist), or the
        // record would be unreadable because a fingerprint may only name a path the record writes.
        payload["SourceFingerprints"]![0]!["FilePath"] = @"C:\elsewhere\Tampered.cs";
        File.WriteAllText(payloadPath, payload.ToJsonString());

        CollectionAssert.AreEqual(new[] { @"C:\elsewhere\Tampered.cs" }, store.PeekChangedPaths(token)!.ToArray());
        Assert.AreEqual(
            @"C:\elsewhere\Tampered.cs",
            store.Retrieve(token, null)!.Value.Mutations.Single().FilePath,
            "Retrieve claims the persisted record, so the peek must agree with it.");
    }

    [TestMethod]
    public void PeekChangedPaths_Persistent_UnknownWhenRecordIsGoneEvenIfMemoryStillHoldsTheEntry()
    {
        var store = CreatePersistentStore();
        var removed = store.Store("ws-1", 1, "removed", [new CompositeFileMutation(@"C:\proj\A.cs", "// a")]);
        File.Delete(PayloadPath(removed));
        var claimed = store.Store("ws-1", 1, "claimed", [new CompositeFileMutation(@"C:\proj\B.cs", "// b")]);
        Assert.IsNotNull(store.Retrieve(claimed, null));

        Assert.IsNull(store.PeekWorkspaceId(removed), "The workspace peek is as disk-authoritative as the write-set peek.");
        Assert.IsNull(store.PeekChangedPaths(removed));
        Assert.IsNull(store.PeekChangedPaths(claimed));
    }

    private CompositePreviewStore CreatePersistentStore()
    {
        var ttl = TimeSpan.FromMinutes(5);
        return new CompositePreviewStore(
            20,
            ttl,
            new PersistentCompositeStorage(_root, ttl),
            new CompositeWorkspaceIdentityResolver(new RecordingWorkspaceManager { AdmittedWorkspaceIds = ["ws-1"] }));
    }

    private string PayloadPath(string token) =>
        Directory.EnumerateFiles(_root, token + ".json", SearchOption.AllDirectories).Single();
}
