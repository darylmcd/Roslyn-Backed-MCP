using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Roslyn.Services;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PromptSmokeTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static SecurityDiagnosticService SecurityService { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
        SecurityService = new SecurityDiagnosticService(
            DiagnosticService,
            WorkspaceManager,
            NullLogger<SecurityDiagnosticService>.Instance);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SuggestRefactoring_Returns_Text()
    {
        var path = FindDocumentPath("AnimalService.cs");
        var messages = (await RoslynPrompts.SuggestRefactoring(
            WorkspaceManager,
            SymbolSearchService,
            WorkspaceId,
            path,
            startLine: null,
            endLine: null,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, messages.Count);
        Assert.IsFalse(string.IsNullOrWhiteSpace(GetText(messages[0])));
    }

    [TestMethod]
    public async Task DiscoverCapabilities_Returns_Tools_List()
    {
        var messages = (await RoslynPrompts.DiscoverCapabilities("navigation")).ToList();
        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);
        StringAssert.Contains(text, "server");
    }

    [TestMethod]
    public async Task SecurityReview_Returns_Text()
    {
        var messages = (await RoslynPrompts.SecurityReview(
            SecurityService,
            DependencyAnalysisService,
            WorkspaceId,
            projectName: null,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, messages.Count);
        StringAssert.Contains(GetText(messages[0]), "security");
    }

    [TestMethod]
    public async Task DeadCodeAudit_Returns_Text()
    {
        var messages = (await RoslynPrompts.DeadCodeAudit(
            UnusedCodeAnalyzer,
            WorkspaceId,
            projectName: null,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, messages.Count);
        StringAssert.Contains(GetText(messages[0]), "dead");
    }

    [TestMethod]
    public async Task ExplainError_Returns_Text()
    {
        var path = FindDocumentPath("Program.cs");
        var messages = (await RoslynPrompts.ExplainError(
            DiagnosticService,
            WorkspaceManager,
            WorkspaceId,
            diagnosticId: "CS0000",
            filePath: path,
            line: 1,
            column: 1,
            CancellationToken.None)).ToList();
        Assert.AreEqual(1, messages.Count);
        Assert.IsFalse(string.IsNullOrWhiteSpace(GetText(messages[0])));
    }

    private static string GetText(PromptMessage message) =>
        (message.Content as TextContentBlock)?.Text
        ?? throw new AssertFailedException("Expected text content.");

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;

        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }
}
