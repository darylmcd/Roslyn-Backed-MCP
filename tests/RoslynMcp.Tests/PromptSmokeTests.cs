using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
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
        var msBuildEvaluation = new MsBuildEvaluationService(WorkspaceManager);
        SecurityService = new SecurityDiagnosticService(
            DiagnosticService,
            WorkspaceManager,
            msBuildEvaluation,
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

    // dr-9-14-drift-001-param-name-mismatch-vs-experimental-p: guard against doc↔impl drift.
    // The `discover_capabilities` prompt's sole user-facing parameter must be named
    // `taskCategory` so callers following the appendix in
    // `ai_docs/prompts/experimental-promotion-exercise.md` (§7c.4) succeed.
    [TestMethod]
    public void DiscoverCapabilities_Parameter_Name_Matches_Experimental_Promotion_Appendix()
    {
        const string expectedParameterName = "taskCategory";

        var method = typeof(RoslynPrompts).GetMethod(
            nameof(RoslynPrompts.DiscoverCapabilities),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException(
                $"Could not find {nameof(RoslynPrompts)}.{nameof(RoslynPrompts.DiscoverCapabilities)}.");

        var parameterNames = method.GetParameters().Select(p => p.Name).ToArray();

        CollectionAssert.Contains(
            parameterNames,
            expectedParameterName,
            $"discover_capabilities must expose parameter '{expectedParameterName}' to match the " +
            "appendix in ai_docs/prompts/experimental-promotion-exercise.md. " +
            $"Actual parameters: [{string.Join(", ", parameterNames)}]");
    }

    [TestMethod]
    public async Task SecurityReview_Returns_Text()
    {
        var messages = (await RoslynPrompts.SecurityReview(
            SecurityService,
            NuGetDependencyService,
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

    // dr-9-7-bug-json-parse-surfaces-stack-trace: malformed parametersJson used to leak a
    // JsonException stack trace as "InternalError"; the wrapped tool now re-throws as
    // ArgumentException which ToolErrorHandler maps to "InvalidArgument".
    [TestMethod]
    public async Task GetPromptText_MalformedJson_ReturnsStructuredInvalidArgument()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var json = await ToolExecutionTestHarness.RunAsync(
            "get_prompt_text",
            () => PromptShimTools.GetPromptText(
                services,
                promptName: "discover_capabilities",
                parametersJson: "{not valid json",
                CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("get_prompt_text", doc.RootElement.GetProperty("tool").GetString());

        var message = doc.RootElement.GetProperty("message").GetString() ?? string.Empty;
        StringAssert.Contains(message, "parametersJson is not valid JSON",
            "Error message should name the offending parameter.");
        Assert.IsFalse(message.Contains(" at System."),
            "Envelope message must not contain raw .NET stack-trace frames.");
    }

    [TestMethod]
    public async Task GetPromptText_NonObjectJson_ReturnsStructuredInvalidArgument()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var json = await ToolExecutionTestHarness.RunAsync(
            "get_prompt_text",
            () => PromptShimTools.GetPromptText(
                services,
                promptName: "discover_capabilities",
                parametersJson: "[1, 2, 3]",
                CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString() ?? string.Empty,
            "must be a JSON object");
    }

    [TestMethod]
    public async Task GetPromptText_ParameterTypeMismatch_ReturnsStructuredInvalidArgument()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        // discover_capabilities takes a string parameter "taskCategory"; passing an integer forces
        // the per-parameter JsonException path at the JsonSerializer.Deserialize call.
        var json = await ToolExecutionTestHarness.RunAsync(
            "get_prompt_text",
            () => PromptShimTools.GetPromptText(
                services,
                promptName: "discover_capabilities",
                parametersJson: "{\"taskCategory\": 123}",
                CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean());
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString() ?? string.Empty,
            "could not be deserialized");
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
