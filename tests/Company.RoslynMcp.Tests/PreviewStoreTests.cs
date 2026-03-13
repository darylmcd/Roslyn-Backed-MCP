using Company.RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Tests;

[TestClass]
public class PreviewStoreTests
{
    private PreviewStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _store = new PreviewStore();
    }

    [TestMethod]
    public void Store_And_Retrieve_Returns_Entry()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store(solution, 1, "test");
        var result = _store.Retrieve(token, 1);

        Assert.IsNotNull(result);
        Assert.AreEqual("test", result.Value.Description);
        workspace.Dispose();
    }

    [TestMethod]
    public void Retrieve_With_Wrong_Version_Returns_Null()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store(solution, 1, "test");
        var result = _store.Retrieve(token, 2);

        Assert.IsNull(result);
        workspace.Dispose();
    }

    [TestMethod]
    public void Retrieve_After_Invalidate_Returns_Null()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store(solution, 1, "test");
        _store.Invalidate(token);
        var result = _store.Retrieve(token, 1);

        Assert.IsNull(result);
        workspace.Dispose();
    }

    [TestMethod]
    public void InvalidateAll_Clears_All_Entries()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token1 = _store.Store(solution, 1, "test1");
        var token2 = _store.Store(solution, 1, "test2");

        _store.InvalidateAll();

        Assert.IsNull(_store.Retrieve(token1, 1));
        Assert.IsNull(_store.Retrieve(token2, 1));
        workspace.Dispose();
    }

    [TestMethod]
    public void Retrieve_With_Invalid_Token_Returns_Null()
    {
        var result = _store.Retrieve("nonexistent", 1);
        Assert.IsNull(result);
    }
}
