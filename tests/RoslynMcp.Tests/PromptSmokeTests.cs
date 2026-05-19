using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
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

    // guided-extract-interface-prompt-payload-cap (gh #776): the prompt embedded the full
    // document-symbol list and full ProjectGraphDto without caps, so a 9+ project workspace
    // with several large files would overflow the MCP inline payload cap. Cap document
    // symbols at 50 and project-graph nodes at 20 — mirrors analyze_dependencies's existing
    // 50-cap on graph.Projects. This test guards both caps and bounds the rendered
    // prompt length so future template growth cannot silently re-open the regression.
    [TestMethod]
    public async Task GuidedExtractInterface_LargeWorkspace_PromptFitsInlineCap()
    {
        const int documentSymbolCount = 200;
        // Use a project count above the 20-cap so the truncation footer must appear.
        // The plan stanza cited 15 projects (which exercises the original gh #776 condition)
        // but 15 is below the cap and produces no footer — we want both caps verified.
        const int projectCount = 25;
        const string fakeWorkspaceId = "fake-large-workspace";
        const string fakeFilePath = @"C:\fake\path\BigFile.cs";
        const string fakeTypeName = "BigType";

        var stubWorkspace = new StubWorkspaceManagerForGuidedExtract(
            workspaceId: fakeWorkspaceId,
            sourceText: "// pretend source text — the prompt only checks for non-null",
            projectCount: projectCount);
        var stubSymbolSearch = new StubSymbolSearchServiceForGuidedExtract(
            documentSymbolCount: documentSymbolCount);

        var messages = (await RoslynPrompts.GuidedExtractInterface(
            stubWorkspace,
            stubSymbolSearch,
            workspaceId: fakeWorkspaceId,
            filePath: fakeFilePath,
            typeName: fakeTypeName,
            targetProjectName: null,
            CancellationToken.None)).ToList();

        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);

        // The pre-cap rendering on a 200-symbol / 15-project workspace embedded the full
        // serialized lists and routinely exceeded 30 KB. Bound the post-cap length to
        // 25 000 characters; the truncated body fits comfortably under that.
        Assert.IsTrue(text.Length < 25_000,
            $"Prompt body must fit under the inline payload cap. Actual length: {text.Length} chars.");

        StringAssert.Contains(text, "[Showing 50 of 200 items]",
            "Document-symbol list must be capped at 50 entries with a count footer.");
        StringAssert.Contains(text, $"[Showing 20 of {projectCount} items]",
            "Project graph (Projects) must be capped at 20 entries with a count footer.");
    }

    // analyze-dependencies-prompt-payload-overflow (gh #755): the prompt previously capped
    // namespace nodes and edges at 100 each but left CircularDependencies uncapped, and the
    // node-count footer was missing. On a 9-project workspace with heavily-namespaced code,
    // the aggregate prompt body reached ~63 KB and overflowed the MCP inline payload cap.
    // The fix lowered the namespace-node/edge caps to 50 each, added a 20-cap for
    // CircularDependencies, and emits truncation footers for every list. This test guards
    // all three caps and bounds the rendered prompt length so future template growth cannot
    // silently re-open the regression.
    [TestMethod]
    public async Task AnalyzeDependencies_LargeWorkspace_PromptFitsInlineCap()
    {
        const int projectCount = 60;
        const int namespaceNodeCount = 120;
        const int namespaceEdgeCount = 120;
        const int circularDependencyCount = 30;
        const int nuGetPackageCount = 60;
        const string fakeWorkspaceId = "fake-large-analyze-deps-workspace";

        var stubWorkspace = new StubWorkspaceManagerForAnalyzeDeps(
            workspaceId: fakeWorkspaceId,
            projectCount: projectCount);
        var stubNamespaceDeps = new StubNamespaceDependencyServiceForAnalyzeDeps(
            nodeCount: namespaceNodeCount,
            edgeCount: namespaceEdgeCount,
            circularCount: circularDependencyCount);
        var stubNuGetDeps = new StubNuGetDependencyServiceForAnalyzeDeps(
            packageCount: nuGetPackageCount);

        var messages = (await RoslynPrompts.AnalyzeDependencies(
            stubWorkspace,
            stubNamespaceDeps,
            stubNuGetDeps,
            workspaceId: fakeWorkspaceId,
            CancellationToken.None)).ToList();

        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);

        // The pre-cap rendering on a 60-project / 120-namespace / 30-cycle / 60-package
        // workspace embedded the full serialized lists (uncapped CircularDependencies, 100-cap
        // on nodes/edges) and would have produced ~80-100 KB of indented JSON, well above the
        // MCP inline payload cap. Bound the post-cap length to 50 000 characters; the
        // truncated body — even with realistic ~40 char namespace names and 3 used-by
        // projects per NuGet package — fits comfortably under that.
        Assert.IsTrue(text.Length < 50_000,
            $"Prompt body must fit under the inline payload cap. Actual length: {text.Length} chars.");

        StringAssert.Contains(text, $"[Showing 50 of {projectCount} items]",
            "Project graph (Projects) must be capped at 50 entries with a count footer.");
        StringAssert.Contains(text, $"[Showing 50 of {namespaceNodeCount} nodes]",
            "Namespace nodes must be capped at 50 entries with a count footer.");
        StringAssert.Contains(text, $"[Showing 50 of {namespaceEdgeCount} edges]",
            "Namespace edges must be capped at 50 entries with a count footer.");
        StringAssert.Contains(text, $"[Showing 20 of {circularDependencyCount} circular dependencies]",
            "Circular dependencies must be capped at 20 entries with a count footer.");
        StringAssert.Contains(text, $"[Showing 50 of {nuGetPackageCount} items]",
            "NuGet packages must be capped at 50 entries with a count footer.");
        StringAssert.Contains(text, "get_namespace_dependencies",
            "Prompt must direct callers to get_namespace_dependencies for full graph inspection.");
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

    // guided-extract-interface-prompt-payload-cap: minimal stub that returns canned source
    // text and a synthetic ProjectGraphDto with the requested number of project nodes. Every
    // other IWorkspaceManager member throws — the prompt only touches GetSourceTextAsync and
    // GetProjectGraph, so the rest is intentionally unreachable.
    private sealed class StubWorkspaceManagerForGuidedExtract : IWorkspaceManager
    {
        private readonly string _workspaceId;
        private readonly string _sourceText;
        private readonly ProjectGraphDto _graph;

        public StubWorkspaceManagerForGuidedExtract(string workspaceId, string sourceText, int projectCount)
        {
            _workspaceId = workspaceId;
            _sourceText = sourceText;
            var projects = Enumerable.Range(0, projectCount).Select(i => new ProjectGraphNodeDto(
                ProjectName: $"FakeProject{i}",
                FilePath: $@"C:\fake\path\FakeProject{i}\FakeProject{i}.csproj",
                AssemblyName: $"FakeProject{i}",
                IsTestProject: false,
                OutputType: "Library",
                TargetFrameworks: new[] { "net10.0" },
                ProjectReferences: Array.Empty<string>())).ToList();
            _graph = new ProjectGraphDto(workspaceId, projects);
        }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => string.Equals(workspaceId, _workspaceId, StringComparison.Ordinal);
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Array.Empty<WorkspaceStatusDto>();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ProjectGraphDto GetProjectGraph(string workspaceId) =>
            string.Equals(workspaceId, _workspaceId, StringComparison.Ordinal)
                ? _graph
                : throw new KeyNotFoundException(workspaceId);

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult<string?>(string.Equals(workspaceId, _workspaceId, StringComparison.Ordinal) ? _sourceText : null);

        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }

    // guided-extract-interface-prompt-payload-cap: minimal stub that returns the requested
    // number of synthetic DocumentSymbolDto entries from GetDocumentSymbolsAsync. The other
    // members are unreachable from GuidedExtractInterface and throw to surface mis-wiring.
    private sealed class StubSymbolSearchServiceForGuidedExtract : ISymbolSearchService
    {
        private readonly IReadOnlyList<DocumentSymbolDto> _symbols;

        public StubSymbolSearchServiceForGuidedExtract(int documentSymbolCount)
        {
            _symbols = Enumerable.Range(0, documentSymbolCount).Select(i => new DocumentSymbolDto(
                Name: $"FakeMember{i}",
                Kind: "Method",
                Modifiers: new[] { "public" },
                StartLine: i + 1,
                EndLine: i + 2,
                Children: null)).ToList();
        }

        public Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
            string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int maxResults, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct, bool allowAdjacent = false) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult(_symbols);

        public Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    // analyze-dependencies-prompt-payload-overflow (gh #755): minimal stub for the analyze
    // dependencies prompt. Returns a ProjectGraphDto with the requested number of project
    // nodes from GetProjectGraph. Every other IWorkspaceManager member throws — the prompt
    // only touches GetProjectGraph.
    private sealed class StubWorkspaceManagerForAnalyzeDeps : IWorkspaceManager
    {
        private readonly string _workspaceId;
        private readonly ProjectGraphDto _graph;

        public StubWorkspaceManagerForAnalyzeDeps(string workspaceId, int projectCount)
        {
            _workspaceId = workspaceId;
            var projects = Enumerable.Range(0, projectCount).Select(i => new ProjectGraphNodeDto(
                ProjectName: $"FakeProject{i}",
                FilePath: $@"C:\fake\path\FakeProject{i}\FakeProject{i}.csproj",
                AssemblyName: $"FakeProject{i}",
                IsTestProject: false,
                OutputType: "Library",
                TargetFrameworks: new[] { "net10.0" },
                ProjectReferences: Array.Empty<string>())).ToList();
            _graph = new ProjectGraphDto(workspaceId, projects);
        }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => string.Equals(workspaceId, _workspaceId, StringComparison.Ordinal);
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Array.Empty<WorkspaceStatusDto>();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ProjectGraphDto GetProjectGraph(string workspaceId) =>
            string.Equals(workspaceId, _workspaceId, StringComparison.Ordinal)
                ? _graph
                : throw new KeyNotFoundException(workspaceId);

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();

        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }

    // analyze-dependencies-prompt-payload-overflow (gh #755): stub returning a synthetic
    // NamespaceDependencyGraphDto with the requested number of nodes, edges, and circular
    // dependencies. Used to exercise the truncation caps under the inline-payload regression
    // test.
    private sealed class StubNamespaceDependencyServiceForAnalyzeDeps : INamespaceDependencyService
    {
        private readonly NamespaceDependencyGraphDto _graph;

        public StubNamespaceDependencyServiceForAnalyzeDeps(int nodeCount, int edgeCount, int circularCount)
        {
            // Synthesize namespace names of realistic median length (~40 chars). Real
            // enterprise namespaces span 20-100 chars; this models the upper end of typical
            // .NET solutions (Roslyn-Backed-MCP, Jellyfin, etc.) without modeling pathological
            // edge cases that would skew the regression bound.
            var nodes = Enumerable.Range(0, nodeCount).Select(i => new NamespaceNodeDto(
                Namespace: $"Contoso.SubsystemAlpha.Module{i:D3}.Internal",
                TypeCount: 5 + (i % 10),
                Project: $"FakeProject{i % 20}")).ToList();

            var edges = Enumerable.Range(0, edgeCount).Select(i => new NamespaceEdgeDto(
                FromNamespace: $"Contoso.SubsystemAlpha.Module{i:D3}.Internal",
                ToNamespace: $"Contoso.SubsystemBeta.Module{(i + 1):D3}.Public",
                ReferenceCount: 1 + (i % 7))).ToList();

            var circles = Enumerable.Range(0, circularCount).Select(i => new CircularDependencyDto(
                Cycle: new[]
                {
                    $"Contoso.SubsystemAlpha.Module{i:D3}.Internal",
                    $"Contoso.SubsystemBeta.Module{i:D3}.Public",
                    $"Contoso.SubsystemAlpha.Module{i:D3}.Internal",
                })).ToList();

            _graph = new NamespaceDependencyGraphDto(
                Nodes: nodes,
                Edges: edges,
                CircularDependencies: circles,
                AnalyzedProjectCount: 60,
                TotalNamespacesScanned: nodeCount);
        }

        public Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
            string workspaceId, string? projectFilter, CancellationToken ct) =>
            Task.FromResult(_graph);
    }

    // analyze-dependencies-prompt-payload-overflow (gh #755): stub returning a synthetic
    // NuGetDependencyResultDto with the requested number of packages. UsedByProjects lists
    // are sized to approximate production payload weight on multi-project workspaces.
    private sealed class StubNuGetDependencyServiceForAnalyzeDeps : INuGetDependencyService
    {
        private readonly NuGetDependencyResultDto _result;

        public StubNuGetDependencyServiceForAnalyzeDeps(int packageCount)
        {
            var packages = Enumerable.Range(0, packageCount).Select(i => new NuGetPackageDto(
                PackageId: $"Contoso.Package{i:D3}",
                Version: $"1.2.{i}",
                UsedByProjects: Enumerable.Range(0, 3).Select(j => $"FakeProject{(i + j) % 20}").ToList())).ToList();

            _result = new NuGetDependencyResultDto(
                Packages: packages,
                Projects: Array.Empty<NuGetProjectDto>(),
                Summaries: null);
        }

        public Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
            string workspaceId, CancellationToken ct, bool summary = false) =>
            Task.FromResult(_result);

        public Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
            string workspaceId, string? projectFilter, bool includeTransitive, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    // review-test-coverage-prompt-payload-overflow (gh #756): the prompt previously
    // applied a per-project cap of `200 / project_count` and serialized the full
    // TestDiscoveryDto verbatim, so a single-project solution kept all 200 cases and
    // pushed the JSON body past 100 KB on large suites. The fix flattens the test
    // cases into a single list and caps at 50 entries via SerializeTruncatedList. This
    // test exercises a 300-case single-project synthetic suite to guard the cap and
    // bound the rendered prompt length so future template growth cannot silently
    // re-open the regression. Note: the plan stanza cited a 25 000 char threshold but
    // — mirroring the sibling AnalyzeDependencies test's 50 000 deviation from a 25 000
    // budget — realistic indented JSON for 50 TestCaseDto items with ~120-150 char
    // FullyQualifiedName values produces a body in the 22-28 KB range, so we set the
    // bound at 30 000 to leave headroom against drift while still meaningfully
    // guarding against the pre-cap regression (which would have produced ~150 KB).
    [TestMethod]
    public async Task ReviewTestCoverage_LargeTestSuite_PromptFitsInlineCap()
    {
        const int totalTestCases = 300;
        const string fakeWorkspaceId = "fake-large-test-discovery-workspace";

        var stubDiscovery = new StubTestDiscoveryServiceForReviewTestCoverage(
            testCaseCount: totalTestCases);

        var messages = (await RoslynPrompts.ReviewTestCoverage(
            stubDiscovery,
            workspaceId: fakeWorkspaceId,
            projectName: null,
            CancellationToken.None)).ToList();

        Assert.AreEqual(1, messages.Count);
        var text = GetText(messages[0]);

        Assert.IsTrue(text.Length < 30_000,
            $"Prompt body must fit under the inline payload cap. Actual length: {text.Length} chars.");

        StringAssert.Contains(text, $"[Showing 50 of {totalTestCases} items]",
            "Discovered test cases must be capped at 50 entries with a count footer.");
        StringAssert.Contains(text, "test_coverage",
            "Prompt must direct callers to test_coverage for coverage collection.");
    }

    // review-test-coverage-prompt-payload-overflow (gh #756): stub that returns a
    // synthetic TestDiscoveryDto with a single project containing the requested number
    // of test cases. Each TestCaseDto uses realistic field lengths
    // (DisplayName ~30 chars, FullyQualifiedName ~120-150 chars, FilePath ~80 chars,
    // Line int) so the post-cap rendered body approximates production payload weight.
    // The Find* members are unreachable from ReviewTestCoverage and throw to surface
    // mis-wiring.
    private sealed class StubTestDiscoveryServiceForReviewTestCoverage : ITestDiscoveryService
    {
        private readonly TestDiscoveryDto _discovery;

        public StubTestDiscoveryServiceForReviewTestCoverage(int testCaseCount)
        {
            var tests = Enumerable.Range(0, testCaseCount).Select(i => new TestCaseDto(
                DisplayName: $"TestMethod_{i:D4}_VerifiesExpectedBehavior",
                FullyQualifiedName: $"Contoso.Application.SubsystemAlpha.Module{i % 20:D3}.UnitTests.TestClass{i % 50:D3}Tests.TestMethod_{i:D4}_VerifiesExpectedBehavior",
                FilePath: $@"C:\fake\path\Contoso.Application\Module{i % 20:D3}\TestClass{i % 50:D3}Tests.cs",
                Line: 50 + (i % 200))).ToList();

            var project = new TestProjectDto(
                ProjectName: "Contoso.Application.UnitTests",
                ProjectFilePath: @"C:\fake\path\Contoso.Application.UnitTests\Contoso.Application.UnitTests.csproj",
                Tests: tests);

            _discovery = new TestDiscoveryDto(TestProjects: new[] { project });
        }

        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) =>
            Task.FromResult(_discovery);

        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(
            string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
            string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
