using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

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
///   <item>The filter's compatibility delegate forwards success projection to
///         <see cref="StructuredCallContentProjector"/>.</item>
/// </list>
///
/// <para>
/// Error-envelope assertions target <see cref="StructuredCallToolFilter.BuildErrorResult"/>.
/// Success projection behavior is owned by <see cref="StructuredCallContentProjectorTests"/>;
/// this suite retains one thin assertion for the filter's forwarding delegate. The cancellation
/// assertion executes the outer filter with a real <see cref="RequestContext{TParams}"/> so it
/// pins the catch ordering rather than merely documenting it.
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
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;

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
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
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
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
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
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
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
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
        Assert.AreEqual("InternalError", payload.GetProperty("category").GetString());
        Assert.IsFalse(payload.TryGetProperty("exceptionType", out _));
        Assert.IsFalse(payload.TryGetProperty("stackTrace", out _));
        Assert.IsTrue(payload.TryGetProperty("correlationId", out _),
            "InternalError envelopes must retain a safe operator correlation reference.");
    }

    [TestMethod]
    public void BuildErrorResult_ValidateRecentGitChanges_UsesCanonicalStructuredEnvelope()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult(
            "validate_recent_git_changes",
            new InvalidOperationException("injected validation failure"));

        Assert.IsTrue(result.IsError);
        var text = ((TextContentBlock)result.Content![0]).Text;
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
        Assert.AreEqual("validate_recent_git_changes", payload.GetProperty("tool").GetString());
        Assert.AreEqual("InvalidOperation", payload.GetProperty("category").GetString());
        Assert.IsTrue(payload.TryGetProperty("_meta", out _));
    }

    [TestMethod]
    public void BuildErrorResult_NotConnectedInvalidOperationException_EmitsDisconnectedEnvelope()
    {
        // compile-check-not-connected-raw-transport-error-envelope: verifies that an
        // InvalidOperationException("Not connected") from a disconnected PipeStream is
        // classified as category "Disconnected" (not the generic "InvalidOperation") and
        // carries a workspace_reload recovery hint so the LLM can self-correct.
        var transportEx = new InvalidOperationException("Not connected");

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult("compile_check", transportEx);

        Assert.IsTrue(result.IsError,
            "Transport-disconnect errors must surface as IsError=true per SEP-1303.");
        Assert.AreEqual(1, result.Content!.Count);

        var text = ((TextContentBlock)result.Content[0]).Text;
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;

        Assert.AreEqual("Disconnected", payload.GetProperty("category").GetString(),
            "InvalidOperationException('Not connected') must classify as Disconnected, " +
            "not as the generic InvalidOperation category.");
        Assert.AreEqual("compile_check", payload.GetProperty("tool").GetString());
        StringAssert.Contains(payload.GetProperty("message").GetString(), "workspace_reload",
            "Disconnected envelope must include a workspace_reload recovery hint so the LLM " +
            "knows how to restore the session after reconnect.");
    }

    // ── Content-projector forwarding compatibility ───────────────────────────

    [TestMethod]
    public void InjectMetaIntoContent_ForwardsObjectProjection()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        var input = new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = """{"result":"ok"}""" }],
        };

        var result = StructuredCallToolFilter.InjectMetaIntoContent(input, "test_tool");

        Assert.AreSame(input, result,
            "The compatibility delegate must preserve the projector's in-place result contract.");
        var text = ((TextContentBlock)result.Content![0]).Text;
        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement;
        Assert.IsTrue(payload.TryGetProperty("_meta", out var meta),
            "The compatibility delegate must forward object projection to StructuredCallContentProjector.");
        Assert.IsTrue(meta.TryGetProperty("queuedMs", out _));
    }

    // ── OperationCanceledException propagates through the live filter delegate ─

    [TestMethod]
    public async Task Create_OperationCanceledExceptionFromNext_PropagatesUnchanged()
    {
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "structured-filter-cancellation",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "structured-filter-cancellation",
            cancellationToken: CancellationToken.None);
        var context = new RequestContext<CallToolRequestParams>(
            harness.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall },
            new CallToolRequestParams { Name = "some_tool" });
        var expected = new OperationCanceledException("cancelled");
        var invocationCount = 0;
        var handler = StructuredCallToolFilter.Create((_, _) =>
        {
            invocationCount++;
            return ValueTask.FromException<CallToolResult>(expected);
        });

        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await handler(context, CancellationToken.None));

        Assert.AreSame(expected, actual,
            "The filter must rethrow the original cancellation rather than converting it to an error result.");
        Assert.AreEqual(1, invocationCount, "The test must reach the wrapped handler exactly once.");
    }
}
