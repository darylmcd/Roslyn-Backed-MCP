using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Exercises <see cref="BoundedStore{TEntry}"/> eviction via <see cref="ProjectMutationPreviewStore"/>.
/// </summary>
[TestClass]
public sealed class BoundedStoreEvictionTests
{
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
