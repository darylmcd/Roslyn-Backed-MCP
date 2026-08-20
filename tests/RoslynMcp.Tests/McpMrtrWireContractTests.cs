using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Raw-wire contract for the <c>mcp-mrtr-dispatch-contract</c> initiative: server-driven input
/// through <see cref="RequestScopedInputAdapter"/> must be portable across both SDK 2.1 session
/// eras. Mirrors the dual-protocol harness pattern of
/// <see cref="ProtocolVersionResultShapeWireTests"/> (negotiated <c>2025-11-25</c> vs
/// <c>2026-07-28</c>, asserting against <c>harness.RawServerMessages</c>). Pins:
/// <list type="bullet">
///   <item><b>MRTR round trip</b> — under <c>2026-07-28</c>, a tool call missing an allowlisted
///   parameter yields an <c>input_required</c> result on the wire (NOT an <c>isError</c>
///   envelope, which was defect (1): the filter's <c>catch (Exception)</c> used to swallow
///   <see cref="InputRequiredException"/>); the SDK client resolves the embedded elicitation and
///   the retry carrying <c>params.inputResponses</c> completes with a success envelope.</item>
///   <item><b>Stateful compatibility</b> — under <c>2025-11-25</c>, the direct nested
///   <c>elicitation/create</c> leg still round-trips and the final result keeps the legacy shape
///   (no <c>resultType</c> discriminator).</item>
///   <item><b>Sanitized non-accept outcomes</b> — declined and malformed input responses each
///   fall through to the existing schema-hint <c>InvalidArgument</c> envelope with no raw
///   exception text or payload echo.</item>
///   <item><b>Cancellation</b> — cancelling while the adapter's legacy elicitation leg is in
///   flight surfaces as protocol cancellation, never as a tool-error envelope.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class McpMrtrWireContractTests
{
    // The fake tool deliberately reuses the workspace_load name so the recovery pipeline's
    // strict allowlist (workspace_load.path) permits elicitation for the missing parameter.
    private const string ToolName = "workspace_load";
    private const string ElicitedPath = "C:/synthetic/solution.slnx";

    // ── (1) MRTR round trip under 2026-07-28 ─────────────────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_MissingPath_EmitsInputRequiredResult_ThenRetryCompletes()
    {
        var elicitationsHandled = 0;
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) =>
            {
                Interlocked.Increment(ref elicitationsHandled);
                return new ValueTask<ElicitResult>(AcceptedPathResult());
            });
        Assert.AreEqual("2026-07-28", harness.Client.NegotiatedProtocolVersion);

        var prior = harness.RawServerMessages.Count;
        var clientResult = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, elicitationsHandled,
            "The client must resolve exactly one embedded elicitation input request.");
        Assert.IsFalse(clientResult.IsError is true,
            "The retry carrying inputResponses must complete with a success envelope.");

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(2, results);

        // Initial call: an input-required protocol result, not a failed tool call.
        var inputRequired = results[0];
        Assert.AreEqual("input_required", inputRequired.GetProperty("resultType").GetString(),
            "The initial tools/call must terminate in an InputRequiredResult on the wire — an " +
            "isError CallToolResult here means the filter swallowed InputRequiredException.");
        Assert.IsFalse(inputRequired.TryGetProperty("isError", out _));
        var inputRequest = inputRequired
            .GetProperty("inputRequests")
            .GetProperty(RequestScopedInputAdapter.ElicitationInputRequestKey);
        Assert.AreEqual("elicitation/create", inputRequest.GetProperty("method").GetString());
        Assert.IsFalse(inputRequired.TryGetProperty("requestState", out _),
            "The adapter is stateless per round trip: the client re-sends the original " +
            "arguments itself, so no opaque continuation state is published.");

        // Retry: success envelope carrying the elicited value.
        var final = results[1];
        Assert.IsFalse(final.TryGetProperty("isError", out var finalIsError) && finalIsError.GetBoolean());
        StringAssert.Contains(final.GetProperty("content")[0].GetProperty("text").GetString(), ElicitedPath);

        // The MRTR leg must not also send the legacy nested server-to-client request.
        Assert.IsFalse(
            AnyServerRequest(harness.RawServerMessages, prior, RequestMethods.ElicitationCreate),
            "Under MRTR the elicitation rides inside the InputRequiredResult; the server must " +
            "not additionally send a nested elicitation/create request.");
    }

    // ── (2) stateful compatibility under 2025-11-25 ──────────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task LegacySession_MissingPath_UsesDirectElicitation_AndLegacyResultShape()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: "2025-11-25",
            elicitationHandler: (_, _) => new ValueTask<ElicitResult>(AcceptedPathResult()));
        Assert.AreEqual("2025-11-25", harness.Client.NegotiatedProtocolVersion);

        var prior = harness.RawServerMessages.Count;
        var clientResult = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.IsFalse(clientResult.IsError is true);
        Assert.IsTrue(
            AnyServerRequest(harness.RawServerMessages, prior, RequestMethods.ElicitationCreate),
            "A 2025-11-25 session must keep the direct nested elicitation/create recovery leg.");

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(1, results,
            "The stateful leg answers the original request in a single wire result — " +
            "input_required never appears on a legacy session.");
        var final = results[0];
        Assert.IsFalse(final.TryGetProperty("resultType", out _),
            "ApplyProtocolResultShape strips the July 2026 discriminator for legacy sessions.");
        Assert.IsFalse(final.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        StringAssert.Contains(final.GetProperty("content")[0].GetProperty("text").GetString(), ElicitedPath);
    }

    // ── (3) sanitized non-accept outcomes ────────────────────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_DeclinedInputResponse_FallsThroughToSanitizedSchemaHintEnvelope()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) =>
                new ValueTask<ElicitResult>(new ElicitResult { Action = "decline" }));

        var prior = harness.RawServerMessages.Count;
        var clientResult = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(clientResult.IsError is true,
            "A declined input response cannot recover the call; the existing InvalidArgument " +
            "envelope is the contract.");

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(2, results,
            "Initial input_required round trip, then the retry's terminal error envelope.");
        var final = results[1];
        Assert.IsTrue(final.GetProperty("isError").GetBoolean());
        AssertSanitizedInvalidArgumentEnvelope(final);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_MalformedInputResponse_FallsThroughToSanitizedSchemaHintEnvelope()
    {
        var elicitationsHandled = 0;
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) =>
            {
                Interlocked.Increment(ref elicitationsHandled);
                return new ValueTask<ElicitResult>(AcceptedPathResult());
            });

        // Hand-craft the retry leg: a tools/call already carrying an inputResponses entry whose
        // raw value cannot deserialize into an ElicitResult. The SDK client always sends
        // well-formed responses, so this is the only way to drive the malformed classification
        // over the real wire.
        var prior = harness.RawServerMessages.Count;
        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = ToolName,
                    ["arguments"] = new JsonObject(),
                    ["inputResponses"] = new JsonObject
                    {
                        [RequestScopedInputAdapter.ElicitationInputRequestKey] = JsonValue.Create(42),
                    },
                },
            },
            CancellationToken.None);

        Assert.AreEqual(0, elicitationsHandled,
            "A malformed retry response is terminal — the adapter must not restart the flow " +
            "with another input request.");

        var result = JsonSerializer.SerializeToElement(response.Result);
        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType)
                       && resultType.GetString() == "input_required",
            "A malformed response classifies as a sanitized failure, not a fresh input_required.");
        Assert.IsTrue(result.GetProperty("isError").GetBoolean());
        AssertSanitizedInvalidArgumentEnvelope(result);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.IsFalse(text.Contains("JsonException", StringComparison.OrdinalIgnoreCase),
            "Deserialization failure detail must never leak into the envelope.");

        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
    }

    // ── (4) cancellation through the adapter's legacy leg ────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task LegacySession_CancelledDuringElicitation_SurfacesProtocolCancellation_NotToolError()
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingElicitation =
            new TaskCompletionSource<ElicitResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var harness = await CreateHarnessAsync(
            protocolVersion: "2025-11-25",
            elicitationHandler: (_, _) =>
            {
                requestReceived.TrySetResult();
                return new ValueTask<ElicitResult>(pendingElicitation.Task);
            });

        try
        {
            var prior = harness.RawServerMessages.Count;
            using var cts = new CancellationTokenSource();
            var call = harness.Client.CallToolAsync(ToolName, cancellationToken: cts.Token);

            // Only cancel once the elicitation genuinely reached the client, so the adapter's
            // legacy ElicitAsync await is the thing observing cancellation (non-vacuous; same
            // discipline as SymbolDisambiguationElicitationTests).
            await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cts.CancelAsync();

            OperationCanceledException? caught = null;
            try
            {
                var result = await call;
                Assert.Fail(
                    "Expected protocol cancellation, but the call completed with " +
                    $"isError={result.IsError}. A cancelled elicitation must never be " +
                    "converted into a tool result.");
            }
            catch (OperationCanceledException ex)
            {
                // The SDK surfaces TaskCanceledException; catch the base class manually.
                caught = ex;
            }

            Assert.IsNotNull(caught);

            // Release the (now moot) elicitation and prove the server survived the cancelled
            // round trip; this also gives a deterministic wire boundary for the assertion below.
            pendingElicitation.TrySetResult(AcceptedPathResult());
            var followUp = await harness.Client.CallToolAsync(
                ToolName,
                cancellationToken: CancellationToken.None);
            Assert.IsFalse(followUp.IsError is true,
                "The server must stay healthy after a cancelled elicitation round trip.");

            foreach (var result in FindNewResults(harness.RawServerMessages, prior))
            {
                Assert.IsFalse(
                    result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
                    "Cancellation must reach the SDK as OperationCanceledException — never be " +
                    $"downgraded to a tool-error envelope. Saw: {result.GetRawText()}");
            }
        }
        finally
        {
            // Never leave the client's elicitation handler pending — an unanswered handler
            // wedges McpClient.DisposeAsync forever (see InMemoryMcpClientServerHarness remarks).
            pendingElicitation.TrySetResult(new ElicitResult { Action = "cancel" });
        }
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static ElicitResult AcceptedPathResult() => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement(ElicitedPath),
        },
    };

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocolVersion,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> elicitationHandler)
    {
        var services = new ServiceCollection();
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "mrtr-wire-contract-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticWorkspaceLoadTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"mrtr-wire-{protocolVersion ?? "modern"}",
            clientCapabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            clientHandlers: new McpClientHandlers { ElicitationHandler = elicitationHandler },
            disposalFailureContext: "mrtr-wire-contract",
            cancellationToken: CancellationToken.None,
            protocolVersion: protocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static IReadOnlyList<JsonElement> FindNewResults(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount) =>
        rawMessages
            .Skip(priorMessageCount)
            .Select(static rawMessage => JsonNode.Parse(rawMessage))
            .OfType<JsonObject>()
            .Where(static message => message["result"] is not null)
            .Select(static message => JsonSerializer.SerializeToElement(message["result"]))
            .ToArray();

    private static bool AnyServerRequest(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount,
        string method) =>
        rawMessages
            .Skip(priorMessageCount)
            .Select(static rawMessage => JsonNode.Parse(rawMessage))
            .OfType<JsonObject>()
            .Any(message => (string?)message["method"] == method);

    /// <summary>
    /// The redaction contract shared with <c>tool-error-envelope-sensitive-detail-disclosure</c>:
    /// the terminal envelope stays the classified <c>InvalidArgument</c> schema-hint shape with
    /// no exception type names or stack frames.
    /// </summary>
    private static void AssertSanitizedInvalidArgumentEnvelope(JsonElement result)
    {
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.IsNotNull(text);
        StringAssert.Contains(text, "InvalidArgument");
        Assert.IsFalse(text.Contains("InputRequiredException", StringComparison.Ordinal),
            "The MRTR protocol signal must never leak into a client-facing envelope.");
        Assert.IsFalse(text.Contains("   at ", StringComparison.Ordinal),
            "Stack frames must never leak into a client-facing envelope.");
    }

    [McpServerToolType]
    private sealed class SyntheticWorkspaceLoadTools
    {
        // The parameter is optional at the binder level and the missing-value failure is thrown
        // by the handler itself, carrying ParamName = "path". This is the InvalidArgument shape
        // StructuredCallElicitationCoordinator.TryGetMissingParam documents and handles; the
        // SDK 2.1.0 reflection binder reports a missing REQUIRED parameter as
        // ArgumentException(ParamName = "arguments"), which the recovery gate deliberately
        // does not match (it needs the concrete parameter name to consult the allowlist).
        [McpServerTool(Name = ToolName)]
        public static string Load(string? path = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "The arguments dictionary is missing a value for the required parameter 'path'.",
                    nameof(path));
            }

            return JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workspaceId"] = "ws-synthetic",
                ["loadedPath"] = path,
            });
        }
    }
}
