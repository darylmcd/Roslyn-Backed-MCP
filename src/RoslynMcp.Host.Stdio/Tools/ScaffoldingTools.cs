using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Elicitation;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for scaffolding refactorings (scaffold_type / scaffold_test /
/// scaffold_test_batch / scaffold_first_test_file). WS1 phase 1.6 — each shim body
/// delegates to the corresponding <see cref="ToolDispatch"/> helper instead of
/// carrying the 7-line dispatch boilerplate inline. See <c>CodeActionTools</c>
/// (canary, PR #305) and <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c>
/// for the migration rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class ScaffoldingTools
{

    [McpServerTool(Name = "scaffold_type_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "experimental", true, false,
        "Preview scaffolding a new type file in a project."),
     Description("Preview scaffolding a new type file in a project. When baseType or interfaces resolve to an interface and implementInterface is true (default), interface members are emitted as NotImplementedException stubs so the scaffolded class compiles against the interface contract.")]
    public static Task<string> PreviewScaffoldType(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Type name to create")] string typeName,
        [Description("Type kind: class, interface, record, or enum")] string typeKind = "class",
        [Description("Optional: namespace override")] string? @namespace = null,
        [Description("Optional: base type name")] string? baseType = null,
        [Description("Optional: additional interface names to declare on the scaffolded type. Pass as a native JSON array, not a JSON-encoded string. Example: [\"IDisposable\", \"IMyService\"].")] string[]? interfaces = null,
        [Description("When true (default), auto-implement interface members of baseType/interfaces as NotImplementedException stubs; set false to emit an empty class body")] bool implementInterface = true,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidateTypeKind(typeKind);
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => scaffoldingService.PreviewScaffoldTypeAsync(
                workspaceId,
                new ScaffoldTypeDto(projectName, typeName, typeKind, @namespace, baseType, interfaces, implementInterface),
                c),
            ct);
    }

    [McpServerTool(Name = "scaffold_test_batch_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "experimental", true, false,
        "Preview scaffolding multiple test files for related target types in one composite preview."),
     Description("Preview scaffolding test files for multiple target types in a single composite preview. Reuses one workspace snapshot across targets to avoid per-target compilation cost. Apply via apply_composite_preview or the returned token.")]
    public static Task<string> PreviewScaffoldTestBatch(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Test project name or project file path within the loaded workspace")] string testProjectName,
        [Description("Array of targets. Each item: { targetTypeName: string, targetMethodName?: string }")] ScaffoldTestBatchTargetDto[] targets,
        [Description("Test framework: mstest, xunit, nunit, or auto (default — inferred from the test project's PackageReferences)")] string testFramework = "auto",
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => scaffoldingService.PreviewScaffoldTestBatchAsync(
                workspaceId,
                new ScaffoldTestBatchDto(testProjectName, targets ?? [], testFramework),
                c),
            ct);

    [McpServerTool(Name = "scaffold_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "experimental", false, true,
        "Apply a previously previewed type scaffolding operation."),
     Description("Apply a previously previewed type scaffolding operation.")]
    public static Task<string> ApplyScaffoldType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by scaffold_type_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "scaffold_type_apply", c),
            ct);

    [McpServerTool(Name = "scaffold_test_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "stable", true, false,
        "Preview scaffolding a new test file (MSTest, xUnit, or NUnit; auto-detect or specify testFramework)."),
     Description("Preview scaffolding a new test file for a target type. Supports MSTest, xUnit, and NUnit (use testFramework or auto-detect from the test project's package references). When referenceTestFile is provided (or the test project contains a sibling *Tests.cs file — auto-detected by most-recently-modified), class-level attributes, base class, and constructor-injected fixture parameters (xUnit IClassFixture<T> pattern) are replicated onto the scaffolded output so ASP.NET Core integration-test conventions carry over without manual rewrite. Pass an empty string for referenceTestFile to opt out of inference.")]
    public static Task<string> PreviewScaffoldTest(
        RequestContext<CallToolRequestParams> requestContext,
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Test project name or project file path within the loaded workspace")] string testProjectName,
        [Description("Target type name for the generated test")] string targetTypeName,
        [Description("Optional: target method name for the generated test stub")] string? targetMethodName = null,
        [Description("Test framework: mstest, xunit, nunit, or auto (infer from PackageReference in the test csproj)")] string testFramework = "auto",
        [Description("Optional: absolute path to an existing sibling test file whose scaffolding (class attributes, base class, IClassFixture<T> constructor) should be replicated. When omitted, the most-recently-modified *Tests.cs in the target project is used. Pass an empty string to opt out of inference.")] string? referenceTestFile = null,
        [Description("When true on a 2026-07-28 request with MRTR sampling input support, request a sampled Given/When/Then test method name from the MCP client. Legacy and sampling-unsupported requests use the deterministic placeholder. Defaults false.")] bool useSampling = false,
        CancellationToken ct = default)
    {
        var testNameSuggestionProvider = useSampling
            ? new McpSamplingTestNameSuggestionProvider(requestContext)
            : null;

        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => scaffoldingService.PreviewScaffoldTestAsync(
                workspaceId,
                new ScaffoldTestDto(testProjectName, targetTypeName, targetMethodName, testFramework, referenceTestFile, useSampling),
                c,
                testNameSuggestionProvider),
            ct);
    }

    internal sealed class McpSamplingTestNameSuggestionProvider : ITestNameSuggestionProvider
    {
        private readonly RequestContext<CallToolRequestParams> _requestContext;
        private readonly Func<
            RequestContext<CallToolRequestParams>,
            string,
            ILogger?,
            (RequestScopedInputOutcome Outcome, string? Text)> _requestSampling;

        internal McpSamplingTestNameSuggestionProvider(
            RequestContext<CallToolRequestParams> requestContext)
            : this(requestContext, RequestScopedInputAdapter.RequestSampling)
        {
        }

        internal McpSamplingTestNameSuggestionProvider(
            RequestContext<CallToolRequestParams> requestContext,
            Func<
                RequestContext<CallToolRequestParams>,
                string,
                ILogger?,
                (RequestScopedInputOutcome Outcome, string? Text)> requestSampling)
        {
            _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
            _requestSampling = requestSampling ?? throw new ArgumentNullException(nameof(requestSampling));
        }

        public Task<TestNameSuggestionResult> SuggestTestNameAsync(
            ScaffoldTestNameSuggestionContext context,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var (outcome, text) = _requestSampling(
                    _requestContext,
                    BuildSamplingPrompt(context),
                    _requestContext.Services?
                        .GetService<ILoggerFactory>()?
                        .CreateLogger(typeof(McpSamplingTestNameSuggestionProvider).FullName ??
                                      nameof(McpSamplingTestNameSuggestionProvider)));
                ct.ThrowIfCancellationRequested();
                if (outcome == RequestScopedInputOutcome.Unsupported)
                {
                    return Task.FromResult(new TestNameSuggestionResult(
                        null,
                        "useSampling was true but request-scoped sampling was unavailable; emitted the deterministic placeholder test name."));
                }

                if (outcome != RequestScopedInputOutcome.Accepted)
                {
                    return Task.FromResult(new TestNameSuggestionResult(
                        null,
                        "The request-scoped sampling response was malformed; emitted the deterministic placeholder test name."));
                }

                return Task.FromResult(new TestNameSuggestionResult(text));
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not InputRequiredException)
            {
                var diagnostic = UnexpectedExceptionReporting.Report(
                    _requestContext.Services?.GetService<IUnexpectedExceptionReporter>(),
                    ex,
                    UnexpectedExceptionCategory.Scaffolding);
                return Task.FromResult(new TestNameSuggestionResult(
                    null,
                    $"Sampling test-name suggestion failed (correlationId={diagnostic.Public.CorrelationId}); emitted the deterministic placeholder test name."));
            }
        }

        private static string FormatSiblingExamples(IReadOnlyList<string> siblingTestMethodNames)
            => siblingTestMethodNames.Count == 0
                ? "(none)"
                : string.Join(", ", siblingTestMethodNames.Take(6));

        private static string BuildSamplingPrompt(ScaffoldTestNameSuggestionContext context) =>
            "Suggest a Given/When/Then test method name.\n" +
            $"Target type: {context.TargetTypeName}\n" +
            $"Target namespace: {context.TargetNamespace ?? "(global)"}\n" +
            $"Target method: {context.TargetMethodName}\n" +
            $"Signature: {context.TargetMethodSignature ?? context.TargetMethodName}\n" +
            $"Sibling examples: {FormatSiblingExamples(context.SiblingTestMethodNames)}\n" +
            "Format example: LoadIntoSessionAsync_WhenCacheMiss_FallsThroughToColdLoad";
    }

    [McpServerTool(Name = "scaffold_test_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "experimental", false, true,
        "Apply a previously previewed test scaffolding operation."),
     Description("Apply a previously previewed test scaffolding operation.")]
    public static Task<string> ApplyScaffoldTest(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by scaffold_test_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "scaffold_test_apply", c),
            ct);

    [McpServerTool(Name = "scaffold_first_test_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "experimental", true, false,
        "Preview scaffolding the first <Service>Tests.cs fixture for a service that has no existing test file."),
     Description("Preview scaffolding the FIRST <Service>Tests.cs fixture for a service that has no existing test file in the destination test project. Distinct from scaffold_test_preview (which adds a single method-focused test to an existing fixture): this tool inspects the service's constructor + all public methods and emits one fixture with a ClassInitialize / setup hook + one smoke-test per public method. The boilerplate shape is derived from up to 3 most-recently-modified sibling *Tests.cs fixtures so ClassInit / base-class conventions carry over. Resolves the service via metadata name (Namespace.TypeName). Errors when the destination file already exists — use scaffold_test_preview for follow-on tests.")]
    public static Task<string> PreviewScaffoldFirstTestFile(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully-qualified type name of the production service (e.g. RoslynMcp.Roslyn.Services.RestructureService)")] string serviceMetadataName,
        [Description("Optional: name or absolute path of the destination test project. When omitted, the scaffolder picks the unique project that references the service's containing project AND whose name ends in '.Tests'.")] string? testProjectName = null,
        [Description("Test framework: mstest, xunit, nunit, or auto (infer from PackageReference in the test csproj)")] string testFramework = "auto",
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => scaffoldingService.PreviewScaffoldFirstTestFileAsync(
                workspaceId,
                new ScaffoldFirstTestFileDto(serviceMetadataName, testProjectName, testFramework),
                c),
            ct);
}
