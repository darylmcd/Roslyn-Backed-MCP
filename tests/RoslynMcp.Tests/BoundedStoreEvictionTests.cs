using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Exercises <see cref="BoundedStore{TEntry}"/> eviction via <see cref="ProjectMutationPreviewStore"/>.
/// </summary>
[TestClass]
public sealed class BoundedStoreEvictionTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Store_EnforcesCapacityAfterEveryInsertion(int capacity)
    {
        var store = new TestStore(capacity);
        var tokens = new List<string>();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            tokens.Add(store.Add(new TestEntry("ws", now.AddSeconds(i))));
            for (var j = 0; j <= i; j++)
                Assert.AreEqual(j > i - capacity, store.Get(tokens[j]) is not null);
        }
    }

    [TestMethod]
    public async Task Store_ConcurrentBurstRetainsExactlyCapacityEntries()
    {
        const int capacity = 8;
        var store = new TestStore(capacity);
        var now = DateTime.UtcNow;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, 32).Select(async i =>
        {
            await release.Task;
            return store.Add(new TestEntry("ws", now.AddSeconds(i)));
        }).ToArray();
        release.SetResult();
        var tokens = await Task.WhenAll(pending);
        Assert.AreEqual(capacity, tokens.Count(token => store.Get(token) is not null));
    }

    [TestMethod]
    public void Store_ExpiresEntriesBeforeEvictingLiveEntriesAndPreservesNewToken()
    {
        var store = new TestStore(2);
        var now = DateTime.UtcNow;
        var expired = store.Add(new TestEntry("ws", now.AddMinutes(-10)));
        var live = store.Add(new TestEntry("ws", now));
        var newest = store.Add(new TestEntry("ws", now.AddSeconds(-1)));
        Assert.IsNull(store.Get(expired));
        Assert.IsNotNull(store.Get(live));
        Assert.IsNotNull(store.Get(newest));
        var replacement = store.Add(new TestEntry("other", now.AddSeconds(-2)));
        Assert.IsNull(store.Get(newest));
        Assert.IsNotNull(store.Get(replacement));
        store.InvalidateAll("ws");
        Assert.IsNull(store.Get(live));
        Assert.AreEqual("other", store.PeekWorkspaceId(replacement));
        store.Drop(entry => entry.WorkspaceId == "other");
        Assert.IsNull(store.Get(replacement));
        var last = store.Add(new TestEntry("last", now));
        store.Invalidate(last);
        Assert.IsNull(store.Get(last));
        var cleared = store.Add(new TestEntry("last", now));
        store.InvalidateAll();
        Assert.IsNull(store.PeekWorkspaceId(cleared));
    }

    [TestMethod]
    public void Store_EvictsOldestInsertionWhenCreatedAtTiesAfterSlotReuse()
    {
        var store = new TestStore(3);
        var tied = DateTime.UtcNow;
        var first = store.Add(new TestEntry("ws", tied));
        var second = store.Add(new TestEntry("ws", tied));
        var third = store.Add(new TestEntry("ws", tied));
        store.Invalidate(first);
        var fourth = store.Add(new TestEntry("ws", tied)); // reuses the freed dictionary slot
        var fifth = store.Add(new TestEntry("ws", tied));
        Assert.IsNull(store.Get(second), "the oldest surviving insertion must be evicted");
        Assert.IsNotNull(store.Get(third));
        Assert.IsNotNull(store.Get(fourth));
        Assert.IsNotNull(store.Get(fifth));
    }

    private sealed record TestEntry(string WorkspaceId, DateTime CreatedAt) : IBoundedStoreEntry;

    private sealed class TestStore(int capacity) : BoundedStore<TestEntry>(capacity)
    {
        public string Add(TestEntry entry) => StoreEntry(entry);
        public TestEntry? Get(string token) => RetrieveEntry(token);
        public void Drop(Func<TestEntry, bool> predicate) => InvalidateWhere(predicate);
    }

    [TestMethod]
    public void ProjectMutationPreviewStore_Evicts_Oldest_When_Exceeding_MaxEntries()
    {
        const int maxEntries = 2;
        var store = new ProjectMutationPreviewStore(maxEntries);

        var t1 = store.Store("ws", "p1.csproj", "<Project/>", 1, "first");
        var t2 = store.Store("ws", "p2.csproj", "<Project/>", 1, "second");
        var t3 = store.Store("ws", "p3.csproj", "<Project/>", 1, "third");
        var t4 = store.Store("ws", "p4.csproj", "<Project/>", 1, "fourth");

        Assert.IsNull(store.Retrieve(t1), "Oldest entry should be evicted when over cap.");
        Assert.IsNull(store.Retrieve(t2), "Second oldest should be evicted when over cap.");
        Assert.IsNotNull(store.Retrieve(t3));
        Assert.IsNotNull(store.Retrieve(t4));
    }

    [TestMethod]
    public void ProjectMutationPreviewStore_PeekWorkspaceId_Returns_Null_For_Unknown_Token()
    {
        var store = new ProjectMutationPreviewStore(3);
        Assert.IsNull(store.PeekWorkspaceId("nonexistent"));
    }
}
