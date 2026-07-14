using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkflowRecommendationToolsTests
{
    [TestMethod]
    [DataRow("find callers of AnimalService.CountAnimals", "find_references", "rg")]
    [DataRow("show a file outline", "document_symbols", "reading the entire file")]
    [DataRow("quick compile sanity after an edit", "compile_check", "dotnet build")]
    [DataRow("run related tests for this changed file", "test_related_files", "full dotnet test")]
    [DataRow("rename this symbol safely", "rename_preview", "search-and-replace")]
    public async Task RecommendWorkflow_CommonTasks_ReturnsFocusedFirstHop(
        string task,
        string expectedPrimaryTool,
        string expectedAvoid)
    {
        var json = await WorkflowRecommendationTools.RecommendWorkflow(task);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var primaryTools = root.GetProperty("primaryTools")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var avoid = root.GetProperty("avoid")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        CollectionAssert.Contains(primaryTools, expectedPrimaryTool);
        Assert.IsTrue(avoid.Any(item => item?.Contains(expectedAvoid, StringComparison.OrdinalIgnoreCase) == true),
            $"Expected avoid-list item containing '{expectedAvoid}'. Actual: [{string.Join(", ", avoid)}]");
        Assert.AreEqual("workspace_loaded", root.GetProperty("requiredWorkspaceState").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("why").GetString()));
    }

    [TestMethod]
    public async Task RecommendWorkflow_CompileSanity_SteersAwayFromBuildWorkspace()
    {
        var json = await WorkflowRecommendationTools.RecommendWorkflow("compile sanity");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var primaryTools = root.GetProperty("primaryTools").EnumerateArray().Select(e => e.GetString()).ToArray();
        var followUpTools = root.GetProperty("followUpTools").EnumerateArray().Select(e => e.GetString()).ToArray();

        CollectionAssert.Contains(primaryTools, "compile_check");
        CollectionAssert.DoesNotContain(primaryTools, "build_workspace");
        CollectionAssert.DoesNotContain(followUpTools, "build_workspace");
    }
}
