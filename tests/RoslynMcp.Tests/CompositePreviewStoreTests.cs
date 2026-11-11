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
        var first = Path.Combine(_root, "A.cs");
        var second = Path.Combine(_root, "B.cs");
        var token = store.Store(
            "ws-1",
            1,
            "two files",
            [new CompositeFileMutation(first, "// a", CompositeFileMutationMode.Edit),
             new CompositeFileMutation(second, null, CompositeFileMutationMode.Delete)]);

        CollectionAssert.AreEqual(new[] { first, second }, store.PeekChangedPaths(token)!.ToArray());
        Assert.IsNotNull(store.Retrieve(token, null), "The peek must not consume the entry.");
        Assert.IsNull(store.PeekChangedPaths(Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public void PeekChangedPaths_Persistent_ReadsThePersistedRecordNotTheCreatorsMemory()
    {
        var store = CreatePersistentStore();
        var original = Path.Combine(_root, "Original.cs");
        var tampered = Path.Combine(_root, "Tampered.cs");
        var token = store.Store("ws-1", 1, "tampered", [new CompositeFileMutation(original, "// a", CompositeFileMutationMode.Edit)]);
        var payloadPath = PayloadPath(token);
        var payload = JsonNode.Parse(File.ReadAllText(payloadPath))!;
        payload["Mutations"]![0]!["FilePath"] = tampered;
        // A consistent forgery also updates the required fingerprint for the target.
        payload["SourceFingerprints"]![0]!["FilePath"] = tampered;
        File.WriteAllText(payloadPath, payload.ToJsonString());

        CollectionAssert.AreEqual(new[] { tampered }, store.PeekChangedPaths(token)!.ToArray());
        Assert.AreEqual(
            tampered,
            store.Retrieve(token, null)!.Value.Mutations.Single().FilePath,
            "Retrieve claims the persisted record, so the peek must agree with it.");
    }

    [TestMethod]
    public void PeekChangedPaths_Persistent_UnknownWhenRecordIsGoneEvenIfMemoryStillHoldsTheEntry()
    {
        var store = CreatePersistentStore();
        var removed = store.Store("ws-1", 1, "removed", [new CompositeFileMutation(Path.Combine(_root, "A.cs"), "// a", CompositeFileMutationMode.Edit)]);
        File.Delete(PayloadPath(removed));
        var claimed = store.Store("ws-1", 1, "claimed", [new CompositeFileMutation(Path.Combine(_root, "B.cs"), "// b", CompositeFileMutationMode.Edit)]);
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
