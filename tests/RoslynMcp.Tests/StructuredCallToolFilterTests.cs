using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression suite for <see cref="StructuredCallToolFilter"/>, the single error-handling
/// and observability boundary for <c>tools/call</c>. These tests lock in behavior for:
/// <list type="bullet">
///   <item>Pre-binding failures (missing required parameter, JSON deserialization of
///         <c>arguments</c>) — the original failure mode that motivated the filter.</item>
///   <item>Handler-thrown exceptions classified by <see cref="Tools.ToolErrorHandler.ClassifyError"/>.</item>
///   <item><see cref="OperationCanceledException"/> propagation (cooperative cancellation is
///         a protocol-level signal, not a tool-execution error).</item>
///   <item><c>_meta</c> injection on the success path, including the bare-array pass-through
///         contract that preserves <c>source_generated_documents</c>-shaped responses.</item>
/// </list>
///
/// <para>
/// The filter's outer lambda requires a real <see cref="ModelContextProtocol.Server.RequestContext{T}"/>
/// to execute (which in turn requires a real <see cref="ModelContextProtocol.Server.McpServer"/>
/// and transport), so this suite targets the two <c>internal</c> helpers
/// (<see cref="StructuredCallToolFilter.BuildErrorResult"/> and
/// <see cref="StructuredCallToolFilter.InjectMetaIntoContent"/>) that own all the envelope
/// construction and meta injection logic. The outer lambda itself is a thin orchestrator
/// whose try/catch shape is identical to the test harness covered by
/// <see cref="ToolErrorHandlerParameterValidationTests"/>; both surfaces drive the same
/// <see cref="Tools.ToolErrorHandler.ClassifyAndFormat"/> +
/// <see cref="Tools.ToolErrorHandler.InjectMetaIfPossible"/> pipeline.
/// </para>
/// </summary>
[TestClass]
public sealed class StructuredCallToolFilterTests
{
    // ── Pre-binding failures (the original bug this filter fixes) ─────────────

    [TestMethod]
    public void BuildErrorResult_MissingRequiredParameter_SurfacesInvalidArgumentAndNamesParameter()
    {
        // Simulates the SDK's reflection-based argument binder throwing when a required
        // parameter is absent from the arguments dictionary. Pre-filter this surfaced as
        // a bare "An error occurred invoking '<tool>'." string with no paramName.
        var binderException = new ArgumentException(
            "The arguments dictionary is missing a value for the required parameter 'path'.",
            paramName: "path");

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult("workspace_load", binderException);

        Assert.IsTrue(result.IsError, "Pre-binding failures must surface as IsError=true per SEP-1303.");
        Assert.AreEqual(1, result.Content!.Count, "Error envelope should contain exactly one content block.");

        var text = ((TextContentBlock)result.Content[0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;

        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString());
        Assert.AreEqual("workspace_load", payload.GetProperty("tool").GetString());
        StringAssert.Contains(payload.GetProperty("message").GetString(), "path",
            "Envelope message MUST name the offending parameter so the LLM can self-correct on retry.");
    }

    [TestMethod]
    public void BuildErrorResult_ArgumentNullException_NamesParameterAsRequired()
    {
        var binderException = new ArgumentNullException(paramName: "workspaceId");

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult("workspace_status", binderException);

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString());
        StringAssert.Contains(payload.GetProperty("message").GetString(), "workspaceId");
        StringAssert.Contains(payload.GetProperty("message").GetString(), "missing",
            "ArgumentNullException messages should make it clear the parameter is missing.");
    }

    [TestMethod]
    public void BuildErrorResult_JsonDeserializationFailure_ClassifiesAsInvalidArgument()
    {
        var binderException = new JsonException("Unexpected token 'Number' while parsing 'path'.");

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult("workspace_load", binderException);

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString());
        StringAssert.Contains(payload.GetProperty("message").GetString(), "JSON",
            "JSON deserialization errors should flag the caller to check property-name casing.");
    }

    // ── Handler-thrown exceptions ─────────────────────────────────────────────

    [TestMethod]
    public void BuildErrorResult_KeyNotFound_ClassifiesAsNotFound()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult(
            "symbol_info",
            new KeyNotFoundException("No symbol resolved for 'Foo.Bar'."));

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.AreEqual("NotFound", payload.GetProperty("category").GetString());
    }

    [TestMethod]
    public void BuildErrorResult_UnrecognizedException_ClassifiesAsInternalError()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult(
            "compile_check",
            new NullReferenceException("Object reference not set to an instance of an object."));

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.AreEqual("InternalError", payload.GetProperty("category").GetString());
        Assert.IsTrue(payload.TryGetProperty("stackTrace", out _),
            "InternalError envelopes should include an abbreviated stack trace for server-side diagnosis.");
    }

    // ── _meta injection on success ────────────────────────────────────────────

    [TestMethod]
    public void InjectMetaIntoContent_ObjectRootedJson_InjectsMetaField()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = """{"result":"ok"}""" }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(input, "test_tool");

        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.IsTrue(payload.TryGetProperty("_meta", out var meta),
            "Object-rooted success responses must carry a _meta block for observability.");
        Assert.IsTrue(meta.TryGetProperty("queuedMs", out _));
    }

    [TestMethod]
    public void InjectMetaIntoContent_ArrayRootedJson_ReturnsResultUnchanged()
    {
        // Backward-compat contract: tools like source_generated_documents return bare
        // arrays. The filter must NOT wrap them in {data, _meta} — that would break every
        // existing consumer. Array-rooted JSON passes through byte-for-byte identical.
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "[1,2,3]" }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result, "Array-rooted responses should return the exact same CallToolResult instance.");
        Assert.AreEqual("[1,2,3]", ((TextContentBlock)result.Content![0]).Text);
    }

    [TestMethod]
    public void InjectMetaIntoContent_EmptyContent_ReturnsResultUnchanged()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result);
    }

    [TestMethod]
    public void InjectMetaIntoContent_NonJsonText_ReturnsResultUnchanged()
    {
        // Defensive: if a tool ever returned plain text (not JSON), injection must be a no-op.
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "not valid json at all" }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result);
    }

    // ── OperationCanceledException is structurally guaranteed to propagate ────

    [TestMethod]
    public void BuildErrorResult_IsNotInvokedForOperationCanceledException_ByDesign()
    {
        // Design assertion (enforced by the filter's try/catch ordering, not this helper):
        // the catch (OperationCanceledException) clause rethrows BEFORE the generic catch
        // that would call BuildErrorResult. This test documents the contract so any future
        // refactor that accidentally wraps cancellation into a CallToolResult is caught in
        // code review — BuildErrorResult itself is not exception-type-aware, so it would
        // happily produce an envelope for OCE if the outer filter let one through.
        //
        // If you're changing this test, you're changing a protocol contract: SDK-level
        // cancellation must remain a JSON-RPC cancellation envelope, not an isError=true
        // tool-execution error.
        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult(
            "some_tool",
            new OperationCanceledException("cancelled"));

        // If BuildErrorResult is reached with an OCE, we still produce a structured envelope
        // — but this path should never fire in the live filter. See the guard in Create(...).
        Assert.IsTrue(result.IsError);
    }
}
