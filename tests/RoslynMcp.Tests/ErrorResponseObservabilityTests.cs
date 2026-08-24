using System.Text;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// Uses an isolated workspace copy instead of the shared sample so the
// FindReferences_WithUnresolvableHandle assertion (expecting category=NotFound)
// is not racy against the shared-workspace auto-reload path. Under parallel
// class execution, a prior SharedWorkspaceTestBase class can leave the shared
// workspace flagged stale, causing the gate to auto-reload mid-call and
// classify the KeyNotFoundException as WorkspaceReloadedDuringCall instead.
// The isolated copy has no such cross-class pressure.
[TestClass]
public sealed class ErrorResponseObservabilityTests : IsolatedWorkspaceTestBase
{
    private static IsolatedWorkspaceScope _scope = null!;
    private static string WorkspaceId => _scope.WorkspaceId;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _scope = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _scope?.Dispose();
    }

    [TestMethod]
    public async Task FindReferences_WithUnresolvableHandle_ReturnsStructuredNotFoundEnvelope()
    {
        // Fabricated but structurally valid handle: decodes correctly, has a metadata name,
        // but the symbol does not exist in the workspace. Pre-fix this returned
        // {count:0, totalCount:0, references:[]} which the caller could not distinguish
        // from a legitimate "valid handle, zero references" outcome.
        var fakeHandle = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"MetadataName":"NonExistentNamespace.NonExistentType"}"""));

        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                requestContext: null!,
                WorkspaceManager,
                WorkspaceExecutionGate,
                ReferenceService,
                WorkspaceId,
                filePath: null,
                line: null,
                column: null,
                symbolHandle: fakeHandle,
                limit: 100,
                offset: 0,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorProp),
            $"Expected structured error envelope. Actual: {json}");
        Assert.IsTrue(errorProp.GetBoolean(),
            "Error envelope must have error: true.");
        Assert.AreEqual("NotFound", doc.RootElement.GetProperty("category").GetString(),
            "Unresolvable handle should map to NotFound category.");
        Assert.AreEqual("find_references", doc.RootElement.GetProperty("tool").GetString(),
            "Tool field must contain the actual tool name, not 'unknown'.");
    }

    [TestMethod]
    public async Task SymbolInfo_WithNearMissMetadataName_ReturnsClosestMatches()
    {
        var json = await ToolExecutionTestHarness.RunAsync(
            "symbol_info",
            () => SymbolTools.GetSymbolInfo(
                WorkspaceExecutionGate,
                SymbolSearchService,
                WorkspaceId,
                metadataName: "SampleLib.AnimalServicex",
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("error").GetBoolean());
        Assert.AreEqual("NotFound", doc.RootElement.GetProperty("category").GetString());
        Assert.IsTrue(doc.RootElement.TryGetProperty("closestMatches", out var closestMatches),
            $"Near-miss metadataName failures must carry closestMatches. Envelope: {json}");
        Assert.IsTrue(
            closestMatches.EnumerateArray().Take(3).Any(match =>
                string.Equals(match.GetProperty("metadataName").GetString(), "SampleLib.AnimalService", StringComparison.Ordinal)),
            $"Expected SampleLib.AnimalService in the top closest matches. Envelope: {json}");
    }

    [TestMethod]
    public async Task ExtractTypePreview_WithUnknownMemberName_ReturnsBlockingDependencies()
    {
        // extract-type-preview-refusal-missing-blocking-deps: mirrors the closestMatches contract
        // above — an extract_type_preview refusal keeps its InvalidOperation category and prose
        // message, and additionally carries the structured member/reason pairs that produced it.
        // The direct tool call uses an explicitly configured test server; null-server calls are
        // fail-closed and cannot bypass the production path boundary.
        var animalServicePath = _scope.GetPath("SampleLib", "AnimalService.cs");
        var server = await GetPathAuthorizedServerAsync();

        var json = await ToolExecutionTestHarness.RunAsync(
            "extract_type_preview",
            () => TypeExtractionTools.PreviewExtractType(
                server: server,
                WorkspaceExecutionGate,
                TypeExtractionService,
                WorkspaceId,
                animalServicePath,
                typeName: "AnimalService",
                memberNames: ["NoSuchMemberOnAnimalService"],
                newTypeName: "AnimalHelper",
                newFilePath: null,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("error").GetBoolean(),
            $"Expected structured error envelope. Actual: {json}");
        Assert.AreEqual("InvalidOperation", doc.RootElement.GetProperty("category").GetString(),
            "category must be unchanged by the structured-field addition.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("blockingDependencies", out var blocking),
            $"extract_type_preview refusals must carry blockingDependencies. Envelope: {json}");
        var first = blocking.EnumerateArray().First();
        Assert.AreEqual("NoSuchMemberOnAnimalService", first.GetProperty("member").GetString(),
            $"blockingDependencies entries must carry a camelCase 'member' key. Envelope: {json}");
        StringAssert.Contains(first.GetProperty("reason").GetString(), "not found in type 'AnimalService'");
    }

    [TestMethod]
    public async Task Resource_GetWorkspaceStatus_WithUnknownWorkspaceId_PropagatesNotFound()
    {
        // resource-read-protocol-error-semantics-workspace: workspace resource handlers
        // use the success-only ExecuteResourceScopeAsync — the gate's KeyNotFoundException
        // PROPAGATES to ResourceReadResultFilter, which answers on the JSON-RPC error
        // channel (ResourceNotFound/InvalidParams by negotiated era) instead of
        // serializing an error envelope into a "successful" contents body.
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            WorkspaceResources.GetWorkspaceStatus(WorkspaceExecutionGate, WorkspaceManager, "ffffffffffffffffffffffffffffffff", CancellationToken.None));
    }

    [TestMethod]
    public async Task Resource_GetProjects_WithUnknownWorkspaceId_PropagatesNotFound()
    {
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            WorkspaceResources.GetProjects(WorkspaceExecutionGate, WorkspaceManager, "ffffffffffffffffffffffffffffffff", CancellationToken.None));
    }

    // inv-arg-envelope-schema-hint: cold-context callers (parallel-mode subagents) cannot
    // read prior-turn calls, so an InvalidArgument envelope must carry enough schema text
    // to compose a re-call without round-tripping through server_info. The hint is sourced
    // from the live tool catalog via reflection — these tests pin the envelope contract
    // and the cold-cache resolution path.

    [TestMethod]
    public void InvalidArgument_KnownParam_EmitsSchemaHintNamingThatParameter()
    {
        var ex = new ArgumentException("Missing 'path'.", paramName: "path");
        var json = ToolErrorHandler.ClassifyAndFormat(ex, "workspace_load");

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("workspace_load", doc.RootElement.GetProperty("tool").GetString());
        Assert.IsTrue(doc.RootElement.TryGetProperty("schemaHint", out var hintProp),
            $"InvalidArgument envelope with known ParamName must carry schemaHint. Envelope: {json}");
        var hint = hintProp.GetString();
        Assert.IsNotNull(hint);
        StringAssert.Contains(hint, "workspace_load(",
            "schemaHint must lead with the tool signature.");
        StringAssert.Contains(hint, "path",
            "schemaHint must name the failing parameter.");
    }

    [TestMethod]
    public void InvalidArgument_NullParam_EmitsToolLevelSchemaHintListingAllParameters()
    {
        // ParamName is unknown (e.g. JSON deserialization fails before binding picks a
        // parameter). The envelope falls back to a tool-level signature so the caller
        // can still see what shape the tool accepts.
        var ex = new System.Text.Json.JsonException("Unexpected token at line 1.");
        var json = ToolErrorHandler.ClassifyAndFormat(ex, "find_references");

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.IsTrue(doc.RootElement.TryGetProperty("schemaHint", out var hintProp),
            $"InvalidArgument envelope must carry schemaHint even when ParamName is null. Envelope: {json}");
        var hint = hintProp.GetString();
        Assert.IsNotNull(hint);
        StringAssert.Contains(hint, "find_references(",
            "Tool-level schemaHint must lead with the tool signature.");
        // find_references accepts a workspaceId plus several locator alternatives — at
        // least one of these must surface in the fallback hint.
        Assert.IsTrue(
            hint.Contains("workspaceId") || hint.Contains("metadataName") || hint.Contains("filePath"),
            $"Tool-level schemaHint must list user-facing parameters. Got: {hint}");
    }

    [TestMethod]
    public void InvalidArgument_SchemaHint_FormatsNullableValueTypesWithSingleQuestionMark()
    {
        var knownParamJson = ToolErrorHandler.ClassifyAndFormat(
            new ArgumentException("Bad prewarm.", paramName: "prewarm"),
            "workspace_load");

        using var knownParamDoc = JsonDocument.Parse(knownParamJson);
        var knownParamHint = knownParamDoc.RootElement.GetProperty("schemaHint").GetString() ?? string.Empty;
        StringAssert.Contains(knownParamHint, "prewarm: bool?");
        Assert.IsFalse(knownParamHint.Contains("bool??", StringComparison.Ordinal),
            $"Nullable bool parameter must not render a doubled marker. Got: {knownParamHint}");

        var toolLevelJson = ToolErrorHandler.ClassifyAndFormat(
            new JsonException("Unexpected token at line 1."),
            "go_to_definition");

        using var toolLevelDoc = JsonDocument.Parse(toolLevelJson);
        var toolLevelHint = toolLevelDoc.RootElement.GetProperty("schemaHint").GetString() ?? string.Empty;
        StringAssert.Contains(toolLevelHint, "line: int?");
        Assert.IsFalse(toolLevelHint.Contains("int??", StringComparison.Ordinal),
            $"Nullable int parameter must not render a doubled marker. Got: {toolLevelHint}");
    }

    [TestMethod]
    public void InvalidArgument_WorkspaceLifecycleSchemaHints_DoNotExposeLoggerFactory()
    {
        foreach (var toolName in new[] { "workspace_load", "workspace_reload", "workspace_close" })
        {
            var json = ToolErrorHandler.ClassifyAndFormat(
                new JsonException("Unexpected token at line 1."),
                toolName);

            using var doc = JsonDocument.Parse(json);
            var hint = doc.RootElement.GetProperty("schemaHint").GetString() ?? string.Empty;
            Assert.IsFalse(
                hint.Contains("loggerFactory", StringComparison.OrdinalIgnoreCase),
                $"{toolName} schemaHint must not expose DI-only ILoggerFactory parameters. Got: {hint}");
        }
    }

    [TestMethod]
    public void InvalidArgument_UnknownTool_OmitsSchemaHintRatherThanEmittingNull()
    {
        // Resource URIs and unknown tool names should NOT emit a stray schemaHint — the
        // envelope's downstream JSON parsers in observability tools rely on the field
        // being absent rather than null when no hint is available.
        var ex = new ArgumentException("Bad input.", paramName: "whatever");
        var json = ToolErrorHandler.ClassifyAndFormat(ex, "this_tool_does_not_exist_anywhere");

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.IsFalse(doc.RootElement.TryGetProperty("schemaHint", out _),
            $"Unknown tool must not emit a schemaHint key. Envelope: {json}");
    }

    [TestMethod]
    public void NonInvalidArgument_NeverEmitsSchemaHint()
    {
        // schemaHint is exclusively for InvalidArgument envelopes — adding it to other
        // categories (NotFound, Timeout, InternalError, …) would conflate parameter-shape
        // guidance with state/runtime issues.
        var ex = new KeyNotFoundException("Workspace 'abc' is not loaded.");
        var json = ToolErrorHandler.ClassifyAndFormat(ex, "workspace_status");

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("NotFound", doc.RootElement.GetProperty("category").GetString());
        Assert.IsFalse(doc.RootElement.TryGetProperty("schemaHint", out _),
            $"Non-InvalidArgument envelope must not carry schemaHint. Got: {json}");
    }
}
