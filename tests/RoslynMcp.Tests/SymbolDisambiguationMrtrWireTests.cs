using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Raw-wire coverage for request-scoped symbol selection. The synthetic tools deliberately use
/// the three public tool names so this suite pins their shared transport contract independently
/// of Roslyn symbol discovery fixtures.
/// </summary>
[TestClass]
public sealed class SymbolDisambiguationMrtrWireTests
{
    private const string _chosenHandle = "roslyn-symbol:v1:chosen";

    [TestMethod]
    [DataRow("symbol_search", null, "2026-07-28", true)]
    [DataRow("go_to_definition", null, "2026-07-28", true)]
    [DataRow("find_references", null, "2026-07-28", true)]
    [DataRow("symbol_search", "2025-11-25", "2025-11-25", false)]
    [DataRow("go_to_definition", "2025-11-25", "2025-11-25", false)]
    [DataRow("find_references", "2025-11-25", "2025-11-25", false)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ChoiceRoundTrip_UsesEraCorrectTransport(
        string toolName,
        string? protocolVersion,
        string expectedVersion,
        bool expectMrtr)
    {
        var promptCount = 0;
        await using var harness = await CreateHarnessAsync(
            protocolVersion,
            new ClientCapabilities { Elicitation = new ElicitationCapability() },
            (_, _) =>
            {
                Interlocked.Increment(ref promptCount);
                return ValueTask.FromResult(AcceptedChoice(_chosenHandle));
            });
        Assert.AreEqual(expectedVersion, harness.Client.NegotiatedProtocolVersion);

        var prior = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(
            toolName,
            new Dictionary<string, object?> { ["allowElicitation"] = true },
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, promptCount);
        Assert.IsFalse(result.IsError is true);
        var text = ((TextContentBlock)result.Content![0]).Text;
        StringAssert.Contains(text, _chosenHandle);
        StringAssert.Contains(text, "chosenViaElicitation");

        var results = FindNewResults(harness.RawServerMessages, prior);
        if (expectMrtr)
        {
            Assert.HasCount(2, results);
            var inputRequired = results[0];
            Assert.AreEqual("input_required", inputRequired.GetProperty("resultType").GetString());
            var inputRequest = inputRequired
                .GetProperty("inputRequests")
                .GetProperty(RequestScopedInputAdapter.SymbolChoiceInputRequestKey);
            Assert.AreEqual(RequestMethods.ElicitationCreate, inputRequest.GetProperty("method").GetString());
            var rawRequest = inputRequest.GetRawText();
            StringAssert.Contains(rawRequest, "roslyn-symbol:v1:first");
            StringAssert.Contains(rawRequest, _chosenHandle);
            Assert.IsFalse(AnyServerRequest(
                harness.RawServerMessages,
                prior,
                RequestMethods.ElicitationCreate));
        }
        else
        {
            Assert.HasCount(1, results);
            Assert.IsTrue(AnyServerRequest(
                harness.RawServerMessages,
                prior,
                RequestMethods.ElicitationCreate));
            Assert.IsFalse(results[0].TryGetProperty("resultType", out _));
        }
    }

    [TestMethod]
    [DataRow("symbol_search")]
    [DataRow("go_to_definition")]
    [DataRow("find_references")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task Mrtr_DeclinedChoice_PreservesAdditiveListFallback(string toolName)
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            new ClientCapabilities { Elicitation = new ElicitationCapability() },
            (_, _) => ValueTask.FromResult(new ElicitResult { Action = "decline" }));

        var result = await harness.Client.CallToolAsync(
            toolName,
            new Dictionary<string, object?> { ["allowElicitation"] = true },
            cancellationToken: CancellationToken.None);

        var text = ((TextContentBlock)result.Content![0]).Text;
        StringAssert.Contains(text, "additiveListFallback");
        StringAssert.Contains(text, "roslyn-symbol:v1:first");
        StringAssert.Contains(text, _chosenHandle);
    }

    [TestMethod]
    [DataRow("symbol_search")]
    [DataRow("go_to_definition")]
    [DataRow("find_references")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task CapableMrtrSession_DefaultOptOut_ReturnsListWithoutInputRequest(string toolName)
    {
        var promptCount = 0;
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            new ClientCapabilities { Elicitation = new ElicitationCapability() },
            (_, _) =>
            {
                Interlocked.Increment(ref promptCount);
                return ValueTask.FromResult(AcceptedChoice(_chosenHandle));
            });
        var prior = harness.RawServerMessages.Count;

        var result = await harness.Client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, promptCount);
        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, "additiveListFallback");
        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(1, results);
        Assert.IsFalse(results[0].TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        Assert.IsFalse(AnyServerRequest(
            harness.RawServerMessages,
            prior,
            RequestMethods.ElicitationCreate));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ElicitationUnsupported_DoesNotSendInputRequest()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            new ClientCapabilities(),
            elicitationHandler: null);
        var prior = harness.RawServerMessages.Count;

        var result = await harness.Client.CallToolAsync(
            "symbol_search",
            new Dictionary<string, object?> { ["allowElicitation"] = true },
            cancellationToken: CancellationToken.None);

        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, "additiveListFallback");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("2025-11-25")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task UrlOnlyElicitation_DoesNotPublishFormRequest(string? protocolVersion)
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion,
            new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability(),
                },
            },
            elicitationHandler: null);
        var prior = harness.RawServerMessages.Count;

        var result = await harness.Client.CallToolAsync(
            "symbol_search",
            new Dictionary<string, object?> { ["allowElicitation"] = true },
            cancellationToken: CancellationToken.None);

        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, "additiveListFallback");
        Assert.IsFalse(AnyServerRequest(
            harness.RawServerMessages,
            prior,
            RequestMethods.ElicitationCreate));
    }

    [TestMethod]
    [DataRow(@"{""action"":""accept"",""content"":{""choice"":42}}")]
    [DataRow(@"{""action"":""accept"",""content"":{""unexpected"":""value""}}")]
    [DataRow(@"{""action"":""accept"",""content"":{""choice"":""roslyn-symbol:v1:chosen"",""unexpected"":""value""}}")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task Mrtr_MalformedAcceptedChoice_PreservesAdditiveListFallback(string responseJson)
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            new ClientCapabilities { Elicitation = new ElicitationCapability() },
            (_, _) => ValueTask.FromResult(AcceptedChoice(_chosenHandle)));

        var result = await SendHandCraftedRetryAsync(harness, JsonNode.Parse(responseJson));

        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        StringAssert.Contains(text, "additiveListFallback");
        Assert.IsFalse(text.Contains("JsonException", StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task Mrtr_NullInputResponse_PreservesSanitizedAdditiveListFallback()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            new ClientCapabilities { Elicitation = new ElicitationCapability() },
            (_, _) => ValueTask.FromResult(AcceptedChoice(_chosenHandle)));

        var result = await SendHandCraftedRetryAsync(harness, inputResponse: null);

        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        StringAssert.Contains(text, "additiveListFallback");
        Assert.IsFalse(text.Contains(nameof(NullReferenceException), StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task Mrtr_StaleChoice_PreservesAdditiveListFallback()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            new ClientCapabilities { Elicitation = new ElicitationCapability() },
            (_, _) => ValueTask.FromResult(AcceptedChoice("roslyn-symbol:v1:stale")));

        var result = await harness.Client.CallToolAsync(
            "find_references",
            new Dictionary<string, object?> { ["allowElicitation"] = true },
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, "additiveListFallback");
    }

    private static ElicitResult AcceptedChoice(string key) => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement>
        {
            ["choice"] = JsonSerializer.SerializeToElement(key),
        },
    };

    private static async Task<JsonElement> SendHandCraftedRetryAsync(
        InMemoryMcpClientServerHarness harness,
        JsonNode? inputResponse)
    {
        var inputResponses = new JsonObject
        {
            [RequestScopedInputAdapter.SymbolChoiceInputRequestKey] = inputResponse,
        };
        var prior = harness.RawServerMessages.Count;
        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = "symbol_search",
                    ["arguments"] = new JsonObject { ["allowElicitation"] = true },
                    ["inputResponses"] = inputResponses,
                },
            },
            CancellationToken.None);
        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior),
            "A retry response is terminal and must not begin another MRTR round trip.");
        return JsonSerializer.SerializeToElement(response.Result);
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocolVersion,
        ClientCapabilities clientCapabilities,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler)
    {
        var services = new ServiceCollection();
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "symbol-mrtr-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticSymbolChoiceTools>();
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"symbol-mrtr-{protocolVersion ?? "modern"}",
            clientCapabilities,
            clientHandlers: elicitationHandler is null
                ? new McpClientHandlers()
                : new McpClientHandlers { ElicitationHandler = elicitationHandler },
            disposalFailureContext: "symbol-mrtr-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion,
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

    [McpServerToolType]
    private sealed class SyntheticSymbolChoiceTools
    {
        [McpServerTool(Name = "symbol_search")]
        public static Task<string> Search(
            RequestContext<CallToolRequestParams> context,
            bool allowElicitation = false,
            CancellationToken cancellationToken = default) =>
            ChooseAsync(context, "symbol_search", allowElicitation, cancellationToken);

        [McpServerTool(Name = "go_to_definition")]
        public static Task<string> GoToDefinition(
            RequestContext<CallToolRequestParams> context,
            bool allowElicitation = false,
            CancellationToken cancellationToken = default) =>
            ChooseAsync(context, "go_to_definition", allowElicitation, cancellationToken);

        [McpServerTool(Name = "find_references")]
        public static Task<string> FindReferences(
            RequestContext<CallToolRequestParams> context,
            bool allowElicitation = false,
            CancellationToken cancellationToken = default) =>
            ChooseAsync(context, "find_references", allowElicitation, cancellationToken);

        private static async Task<string> ChooseAsync(
            RequestContext<CallToolRequestParams> context,
            string toolName,
            bool allowElicitation,
            CancellationToken cancellationToken)
        {
            var options = new[]
            {
                ("roslyn-symbol:v1:first", "First candidate"),
                (_chosenHandle, "Chosen candidate"),
            };
            var chosen = allowElicitation
                ? await ElicitationChoicePrompt.TryElicitChoiceAsync(
                    context,
                    "choice",
                    "Pick a symbol",
                    $"{toolName} returned two candidates.",
                    options,
                    cancellationToken).ConfigureAwait(false)
                : null;

            return chosen is null
                ? JsonSerializer.Serialize(new
                {
                    additiveListFallback = true,
                    candidates = options.Select(static option => option.Item1).ToArray(),
                })
                : JsonSerializer.Serialize(new { chosenHandle = chosen, chosenViaElicitation = true });
        }
    }
}
