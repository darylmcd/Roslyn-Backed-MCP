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

        var token = _store.Store("workspace-1", solution, 1, "test");
        var result = _store.Retrieve(token);

        Assert.IsNotNull(result);
        Assert.AreEqual("test", result.Value.Description);
        Assert.AreEqual("workspace-1", result.Value.WorkspaceId);
        workspace.Dispose();
    }

    [TestMethod]
    public void Retrieve_Returns_Workspace_Version_For_External_Validation()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, 1, "test");
        var result = _store.Retrieve(token);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Value.WorkspaceVersion);
        workspace.Dispose();
    }

    [TestMethod]
    public void Retrieve_After_Invalidate_Returns_Null()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, 1, "test");
        _store.Invalidate(token);
        var result = _store.Retrieve(token);

        Assert.IsNull(result);
        workspace.Dispose();
    }

    [TestMethod]
    public void InvalidateAll_Clears_All_Entries()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token1 = _store.Store("workspace-1", solution, 1, "test1");
        var token2 = _store.Store("workspace-2", solution, 1, "test2");

        _store.InvalidateAll();

        Assert.IsNull(_store.Retrieve(token1));
        Assert.IsNull(_store.Retrieve(token2));
        workspace.Dispose();
    }

    [TestMethod]
    public void Retrieve_With_Invalid_Token_Returns_Null()
    {
        var result = _store.Retrieve("nonexistent");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void InvalidateAll_For_Workspace_Clears_Only_Matching_Entries()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token1 = _store.Store("workspace-1", solution, 1, "test1");
        var token2 = _store.Store("workspace-2", solution, 1, "test2");

        _store.InvalidateAll("workspace-1");

        Assert.IsNull(_store.Retrieve(token1));
        Assert.IsNotNull(_store.Retrieve(token2));
        workspace.Dispose();
    }
}
