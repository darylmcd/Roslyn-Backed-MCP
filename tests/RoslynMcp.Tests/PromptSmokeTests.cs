using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class PromptSmokeTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
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
    // `taskCategory` so callers following Phase 16 of
    // `ai_docs/prompts/deep-review-and-refactor.md` (which exercises `get_prompt_text`
    // against this prompt) succeed. Originally filed against the retired
    // `experimental-promotion-exercise.md` prompt; folded into deep-review Phase 16 in
    // the 2026-04-22 audit-prompts refactor.
    [TestMethod]
    public void DiscoverCapabilities_Parameter_Name_Matches_Deep_Review_Phase_16()
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
            $"discover_capabilities must expose parameter '{expectedParameterName}' to match " +
            "the prompt invocation in ai_docs/prompts/deep-review-and-refactor.md Phase 16. " +
            $"Actual parameters: [{string.Join(", ", parameterNames)}]");
    }

    // get-prompt-text-side-effects-in-rendering: security_review must be a pure template — it
    // MUST NOT invoke ISecurityDiagnosticService or INuGetDependencyService during rendering.
    // The previous implementation eagerly invoked all three security tools (including the
    // network/SDK `dotnet list package --vulnerable` call) on every prompt fetch.
    [TestMethod]
    public void SecurityReview_Returns_Text()
    {
        var messages = RoslynPrompts.SecurityReview(
            workspaceId: WorkspaceId,
            projectName: null).ToList();
        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);
        StringAssert.Contains(text, "security");
        StringAssert.Contains(text, "security_diagnostics",
            "Template must direct the caller to invoke security_diagnostics — the prompt no longer pre-fetches findings.");
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

    // file-lock-aware-prompt-validation-guidance + get-prompt-text-side-effects-in-rendering:
    // the FileLock-bypass guidance is now baked into the static template. The prompt no longer
    // takes a TestRunnerService parameter and never invokes RunTestsAsync — the caller runs
    // `test_run` first and follows the appropriate template branch based on the result envelope.
    // This test guards the template content so the bypass branch can't be removed without
    // breaking the smoke test.
    [TestMethod]
    public void DebugTestFailure_RendersFileLockBypassGuidance()
    {
        var messages = RoslynPrompts.DebugTestFailure(
            workspaceId: "any-workspace",
            projectName: null,
            filter: null).ToList();

        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);

        StringAssert.Contains(text, "infrastructure",
            "Bypass guidance must declare the failure infrastructure-class.");
        StringAssert.Contains(text, "FileLock",
            "Bypass guidance must name the FileLock envelope so the operator can correlate.");
        StringAssert.Contains(text, "compile_check",
            "Bypass guidance must point to a read-side alternative.");
        StringAssert.Contains(text, "workspace_reload",
            "Bypass guidance must instruct the operator to release in-process analyzer-DLL handles.");
        StringAssert.Contains(text, "dotnet build-server shutdown",
            "Bypass guidance must instruct shutting down the build daemon when the holder is unclear.");
    }

    // get-prompt-text-side-effects-in-rendering: debug_test_failure must be a pure template — it
    // MUST NOT accept an ITestRunnerService parameter and MUST NOT invoke RunTestsAsync during
    // rendering. The previous implementation spawned a live `dotnet test` on every prompt fetch.
    // This test guards against regression by verifying the prompt signature is parameter-free of
    // service injection AND by calling it with a workspaceId that would have failed in the old
    // implementation (no live runner present).
    [TestMethod]
    public void DebugTestFailure_DoesNotInvokeTestRunnerService()
    {
        var method = typeof(RoslynPrompts).GetMethod(
            nameof(RoslynPrompts.DebugTestFailure),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException($"Could not find {nameof(RoslynPrompts)}.{nameof(RoslynPrompts.DebugTestFailure)}.");

        var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.IsFalse(parameterTypes.Any(t => t == typeof(ITestRunnerService)),
            "debug_test_failure must not accept ITestRunnerService — that parameter was the side-effect carrier. "
            + $"Actual parameter types: [{string.Join(", ", parameterTypes.Select(t => t.Name))}]");

        // Behavioral guard: calling the prompt with a non-existent workspace id must not throw —
        // the old eager call would have surfaced a workspace-not-found exception from the runner.
        var messages = RoslynPrompts.DebugTestFailure(
            workspaceId: "nonexistent-workspace-id-that-no-runner-could-resolve",
            projectName: null,
            filter: null).ToList();
        Assert.AreEqual(1, messages.Count);
        StringAssert.Contains(GetText(messages[0]), "test_run",
            "Template must direct the caller to invoke test_run — the prompt no longer runs tests itself.");
    }

    // get-prompt-text-side-effects-in-rendering: security_review must be a pure template — it
    // MUST NOT accept ISecurityDiagnosticService or INuGetDependencyService parameters and MUST
    // NOT invoke any security tools during rendering. The previous implementation spawned a
    // `dotnet list package --vulnerable` network/SDK call on every prompt fetch.
    [TestMethod]
    public void SecurityReview_DoesNotInvokeSecurityServices()
    {
        var method = typeof(RoslynPrompts).GetMethod(
            nameof(RoslynPrompts.SecurityReview),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException($"Could not find {nameof(RoslynPrompts)}.{nameof(RoslynPrompts.SecurityReview)}.");

        var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.IsFalse(parameterTypes.Any(t => t == typeof(ISecurityDiagnosticService)),
            "security_review must not accept ISecurityDiagnosticService — that parameter was a side-effect carrier. "
            + $"Actual parameter types: [{string.Join(", ", parameterTypes.Select(t => t.Name))}]");
        Assert.IsFalse(parameterTypes.Any(t => t == typeof(INuGetDependencyService)),
            "security_review must not accept INuGetDependencyService — that parameter was a side-effect carrier "
            + "(the old implementation spawned `dotnet list package --vulnerable` on every prompt fetch). "
            + $"Actual parameter types: [{string.Join(", ", parameterTypes.Select(t => t.Name))}]");

        // Behavioral guard: calling the prompt with a non-existent workspace id must not throw —
        // the old eager call would have surfaced a workspace-not-found exception from the runner.
        var messages = RoslynPrompts.SecurityReview(
            workspaceId: "nonexistent-workspace-id-that-no-service-could-resolve",
            projectName: null).ToList();
        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);
        StringAssert.Contains(text, "security_analyzer_status",
            "Template must direct the caller to invoke security_analyzer_status — the prompt no longer pre-fetches status.");
        StringAssert.Contains(text, "nuget_vulnerability_scan",
            "Template must direct the caller to invoke nuget_vulnerability_scan — the prompt no longer pre-fetches CVE data.");
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
