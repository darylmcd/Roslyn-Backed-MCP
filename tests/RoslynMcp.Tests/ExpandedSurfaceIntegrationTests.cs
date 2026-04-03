using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExpandedSurfaceIntegrationTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task GetCompletions_Returns_Service_Members()
    {
        var programFile = FindDocumentPath("Program.cs");
        var json = await SymbolTools.GetCompletions(
            null!,
            WorkspaceExecutionGate,
            CompletionService,
            WorkspaceId,
            programFile,
            line: 6,
            column: 9,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("Items");
        Assert.IsTrue(items.GetArrayLength() > 0, "Expected completion items.");
        Assert.IsTrue(items.EnumerateArray().Any(item => item.GetProperty("DisplayText").GetString() == "MakeThemSpeak"));
    }

    [TestMethod]
    public async Task GetSyntaxTree_Returns_Hierarchy_Json()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await SyntaxTools.GetSyntaxTree(
            null!,
            WorkspaceExecutionGate,
            SyntaxService,
            WorkspaceId,
            filePath,
            startLine: null,
            endLine: null,
            maxDepth: 2,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("CompilationUnit", doc.RootElement.GetProperty("Kind").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("Children").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task AdvancedAnalysisTools_Return_Expected_Results()
    {
        var namespaceJson = await AdvancedAnalysisTools.GetNamespaceDependencies(
            WorkspaceExecutionGate,
            DependencyAnalysisService,
            WorkspaceId,
            project: "SampleLib",
            circularOnly: false,
            CancellationToken.None);
        using var namespaceDoc = JsonDocument.Parse(namespaceJson);
        Assert.IsTrue(namespaceDoc.RootElement.GetProperty("Nodes").GetArrayLength() > 0);

        var complexityJson = await AdvancedAnalysisTools.GetComplexityMetrics(
            WorkspaceExecutionGate,
            CodeMetricsService,
            WorkspaceId,
            filePath: null,
            project: "SampleLib",
            minComplexity: 1,
            limit: 20,
            CancellationToken.None);
        using var complexityDoc = JsonDocument.Parse(complexityJson);
        Assert.IsTrue(complexityDoc.RootElement.GetProperty("metrics").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task AdvancedAnalysisTools_And_Diagnostics_Support_Pagination()
    {
        var analyzersJson = await AnalyzerInfoTools.ListAnalyzers(
            WorkspaceExecutionGate,
            AnalyzerInfoService,
            WorkspaceId,
            project: null,
            offset: 0,
            limit: 2,
            CancellationToken.None);
        using var analyzersDoc = JsonDocument.Parse(analyzersJson);
        Assert.IsTrue(analyzersDoc.RootElement.GetProperty("totalRules").GetInt32() >= 1);
        Assert.IsTrue(analyzersDoc.RootElement.GetProperty("returnedRules").GetInt32() <= 2);
        Assert.IsTrue(analyzersDoc.RootElement.TryGetProperty("hasMore", out _));

        var diagnosticsJson = await AnalysisTools.GetProjectDiagnostics(
            WorkspaceExecutionGate,
            DiagnosticService,
            WorkspaceId,
            project: "SampleLib",
            file: null,
            severity: null,
            offset: 0,
            limit: 1,
            CancellationToken.None);
        using var diagnosticsDoc = JsonDocument.Parse(diagnosticsJson);
        var returnedDiagnostics = diagnosticsDoc.RootElement.GetProperty("returnedDiagnostics").GetInt32();
        var pagedCount = diagnosticsDoc.RootElement.GetProperty("workspaceDiagnostics").GetArrayLength()
            + diagnosticsDoc.RootElement.GetProperty("compilerDiagnostics").GetArrayLength()
            + diagnosticsDoc.RootElement.GetProperty("analyzerDiagnostics").GetArrayLength();

        Assert.AreEqual(returnedDiagnostics, pagedCount);
        Assert.IsTrue(diagnosticsDoc.RootElement.GetProperty("totalDiagnostics").GetInt32() >= returnedDiagnostics);
    }

    [TestMethod]
    public async Task Reflection_And_Di_Analysis_Run_On_Repo_Solution()
    {
        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");
        var status = await WorkspaceManager.LoadAsync(repositorySolutionPath, CancellationToken.None);

        try
        {
            var reflectionUsages = await CodePatternAnalyzer.FindReflectionUsagesAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);
            var diRegistrations = await DependencyAnalysisService.GetDiRegistrationsAsync(
                status.WorkspaceId,
                "RoslynMcp.Roslyn",
                CancellationToken.None);

            Assert.IsTrue(reflectionUsages.Count > 0, "Expected reflection analysis to complete with results on the repo solution.");
            Assert.IsTrue(diRegistrations.Any(registration =>
                registration.FilePath.EndsWith("ServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)),
                "Expected DI registrations from ServiceCollectionExtensions.cs.");
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
    }

    [TestMethod]
    public void Resources_Return_Machine_Readable_Contracts()
    {
        using var catalogDoc = JsonDocument.Parse(ServerResources.GetServerCatalog());
        Assert.AreEqual("local-first", catalogDoc.RootElement.GetProperty("ProductShape").GetString());
        Assert.IsTrue(catalogDoc.RootElement.GetProperty("Tools").GetArrayLength() > 0);

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
            DependencyAnalysisService,
            WorkspaceId,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, dependencyMessages.Count);
        StringAssert.Contains(GetPromptText(dependencyMessages[0]), "Project Dependency Graph");
    }

    [TestMethod]
    public async Task CodeActionTools_Return_Structured_Results()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var actionsJson = await CodeActionTools.GetCodeActions(
            WorkspaceExecutionGate,
            CodeActionService,
            WorkspaceId,
            filePath,
            startLine: 1,
            startColumn: 1,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        using var actionsDoc = JsonDocument.Parse(actionsJson);
        Assert.IsTrue(actionsDoc.RootElement.TryGetProperty("count", out _));
        Assert.IsTrue(actionsDoc.RootElement.TryGetProperty("actions", out _));
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
        Assert.AreEqual("preview-token", doc.RootElement.GetProperty("PreviewToken").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("Changes").GetArrayLength() == 1);
    }

    [TestMethod]
    public async Task EditTools_And_MultiFileEdit_Apply_Changes_On_Workspace_Copy()
    {
        var workspacePath = CreateSampleSolutionCopy();
        var tempRoot = Path.GetDirectoryName(workspacePath)!;
        var tempWorkspaceId = await LoadWorkspaceCopyAsync(workspacePath);

        try
        {
            var programFile = FindDocumentPath(tempWorkspaceId, "Program.cs");
            var singleEditJson = await EditTools.ApplyTextEdit(
                null!,
                WorkspaceExecutionGate,
                EditService,
                tempWorkspaceId,
                programFile,
                [new TextEditDto(1, 1, 1, 1, "// edited\n")],
                CancellationToken.None);

            using var singleEditDoc = JsonDocument.Parse(singleEditJson);
            Assert.IsTrue(singleEditDoc.RootElement.GetProperty("EditsApplied").GetInt32() == 1);
            StringAssert.Contains(await File.ReadAllTextAsync(programFile), "// edited");

            var animalFile = FindDocumentPath(tempWorkspaceId, "AnimalService.cs");
            var multiEditJson = await MultiFileEditTools.ApplyMultiFileEdit(
                null!,
                WorkspaceExecutionGate,
                EditService,
                tempWorkspaceId,
                [
                    new FileEditsDto(programFile, [new TextEditDto(2, 1, 2, 1, "// second edit\n")]),
                    new FileEditsDto(animalFile, [new TextEditDto(1, 1, 1, 1, "// animal edit\n")])
                ],
                CancellationToken.None);

            using var multiEditDoc = JsonDocument.Parse(multiEditJson);
            Assert.IsTrue(multiEditDoc.RootElement.GetProperty("FilesModified").GetInt32() == 2);
            StringAssert.Contains(await File.ReadAllTextAsync(animalFile), "// animal edit");
        }
        finally
        {
            WorkspaceManager.Close(tempWorkspaceId);
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [TestMethod]
    public async Task TestCoverageTool_Returns_Structured_Response()
    {
        var json = await TestCoverageTools.RunTestCoverage(
            WorkspaceExecutionGate,
            WorkspaceManager,
            DotnetCommandRunner,
            WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("Success", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("Error", out _));
    }

    private static string FindDocumentPath(string name) => FindDocumentPath(WorkspaceId, name);

    private static string FindDocumentPath(string workspaceId, string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
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

    private static async Task<string> LoadWorkspaceCopyAsync(string workspacePath)
    {
        var status = await WorkspaceManager.LoadAsync(workspacePath, CancellationToken.None);
        return status.WorkspaceId;
    }

    private sealed class FakeCodeActionService : RoslynMcp.Core.Services.ICodeActionService
    {
        public Task<IReadOnlyList<CodeActionDto>> GetCodeActionsAsync(
            string workspaceId,
            string filePath,
            int startLine,
            int startColumn,
            int? endLine,
            int? endColumn,
            CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<CodeActionDto>>([new CodeActionDto(0, "Demo action", "Refactor", "demo")]);
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
