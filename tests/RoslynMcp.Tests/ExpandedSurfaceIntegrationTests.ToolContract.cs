using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class ExpandedSurfaceIntegrationTests_ToolContract : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public void Resources_Return_Machine_Readable_Contracts()
    {
        using var catalogDoc = JsonDocument.Parse(ServerResources.GetServerCatalog());
        Assert.AreEqual("local-first", catalogDoc.RootElement.GetProperty("productShape").GetString());
        // dr-9-11-payload-exceeds-mcp-tool-result-cap (PR #188): default catalog now returns
        // a summary with toolCount + toolsResourceTemplate instead of an inline tools array.
        Assert.IsTrue(catalogDoc.RootElement.GetProperty("toolCount").GetInt32() > 0);

        using var templatesDoc = JsonDocument.Parse(ServerResources.GetResourceTemplates());
        Assert.IsTrue(templatesDoc.RootElement.GetProperty("count").GetInt32() >= 1);
        Assert.IsTrue(templatesDoc.RootElement.GetProperty("resources")
            .EnumerateArray()
            .Any(resource => string.Equals(
                resource.GetProperty("uriTemplate").GetString(),
                "roslyn://workspace/{workspaceId}/status",
                StringComparison.Ordinal)));

        using var workspacesDoc = JsonDocument.Parse(WorkspaceResources.GetWorkspaces(WorkspaceManager));
        Assert.IsTrue(workspacesDoc.RootElement.GetProperty("count").GetInt32() >= 1);
    }

    [TestMethod]
    public async Task Prompts_Return_Actionable_Context()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var reviewMessages = (await RoslynPrompts.ReviewFile(
            WorkspaceManager,
            SymbolSearchService,
            DiagnosticService,
            WorkspaceId,
            filePath,
            CancellationToken.None)).ToList();

        Assert.AreEqual(1, reviewMessages.Count);
        var reviewText = GetPromptText(reviewMessages[0]);
        StringAssert.Contains(reviewText, "AnimalService.cs");
        StringAssert.Contains(reviewText, "Perform a thorough code review");

        var dependencyMessages = (await RoslynPrompts.AnalyzeDependencies(
            WorkspaceManager,
            NamespaceDependencyService,
            NuGetDependencyService,
            WorkspaceId,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, dependencyMessages.Count);
        StringAssert.Contains(GetPromptText(dependencyMessages[0]), "Project Dependency Graph");
    }

    [TestMethod]
    public async Task PreviewCodeAction_Serializes_Service_Response()
    {
        var previewJson = await CodeActionTools.PreviewCodeAction(
            WorkspaceExecutionGate,
            new FakeCodeActionService(),
            WorkspaceId,
            filePath: "C:\\temp\\Example.cs",
            startLine: 1,
            startColumn: 1,
            actionIndex: 0,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(previewJson);
        Assert.AreEqual("preview-token", doc.RootElement.GetProperty("previewToken").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("changes").GetArrayLength() == 1);
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;

        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }

    private static string GetPromptText(PromptMessage message)
    {
        return (message.Content as TextContentBlock)?.Text
            ?? throw new AssertFailedException("Prompt content was not a text block.");
    }

    private sealed class FakeCodeActionService : RoslynMcp.Core.Services.ICodeActionService
    {
        public Task<CodeActionListDto> GetCodeActionsAsync(
            string workspaceId,
            string filePath,
            int startLine,
            int startColumn,
            int? endLine,
            int? endColumn,
            CancellationToken ct)
        {
            IReadOnlyList<CodeActionDto> actions = [new CodeActionDto(0, "Demo action", "Refactor", "demo")];
            return Task.FromResult(new CodeActionListDto(actions.Count, Hint: null, actions));
        }

        public Task<RefactoringPreviewDto> PreviewCodeActionAsync(
            string workspaceId,
            string filePath,
            int startLine,
            int startColumn,
            int? endLine,
            int? endColumn,
            int actionIndex,
            CancellationToken ct)
        {
            return Task.FromResult(new RefactoringPreviewDto(
                "preview-token",
                "Demo preview",
                [new FileChangeDto(filePath, "--- before\n+++ after")],
                null));
        }
    }
}
