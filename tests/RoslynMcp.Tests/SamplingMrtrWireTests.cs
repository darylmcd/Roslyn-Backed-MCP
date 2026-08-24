using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>Raw-wire and redaction coverage for request-scoped sampling.</summary>
[TestClass]
public sealed class SamplingMrtrWireTests
{
    private const string _suggestedName = "Load_WhenCacheMiss_ReturnsValue";
    private const string _samplingMethod = "sampling/createMessage";

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ModernSession_SamplingRoundTrip_UsesRequestScopedInput()
    {
        var samplingCount = 0;
#pragma warning disable MCP9005 // Test fixture for the SDK sampling payload carried inside MRTR.
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            samplingHandler: (_, _, _) =>
            {
                Interlocked.Increment(ref samplingCount);
                return ValueTask.FromResult(SamplingResult(_suggestedName));
            });
#pragma warning restore MCP9005

        var prior = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(
            "scaffold_test_preview",
            new Dictionary<string, object?> { ["useSampling"] = true },
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, samplingCount);
        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, _suggestedName);
        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(2, results);
        var inputRequired = results[0];
        Assert.AreEqual("input_required", inputRequired.GetProperty("resultType").GetString());
        var inputRequest = inputRequired
            .GetProperty("inputRequests")
            .GetProperty(RequestScopedInputAdapter.SamplingInputRequestKey);
        Assert.AreEqual(_samplingMethod, inputRequest.GetProperty("method").GetString());
        StringAssert.Contains(inputRequest.GetRawText(), "Given/When/Then");
        Assert.IsFalse(AnyServerRequest(
            harness.RawServerMessages,
            prior,
            _samplingMethod),
            "MRTR sampling must not also issue a deprecated nested sampling request.");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ModernSession_ProductionScaffoldingTool_AdvertisesAndCompletesMrtrSampling()
    {
        var originalWorkspaceId = Guid.NewGuid().ToString("N");
        var concurrentWorkspaceId = Guid.NewGuid().ToString("N");
        var workspaceManager = new SamplingWorkspaceManager(WorkspaceStatus(originalWorkspaceId));
        var samplingCount = 0;
        var scaffoldingService = new RecordingScaffoldingService();
#pragma warning disable MCP9005 // Test fixture for the SDK sampling payload carried inside MRTR.
        await using var harness = await CreateProductionHarnessAsync(
            scaffoldingService,
            (_, _, _) =>
            {
                Interlocked.Increment(ref samplingCount);
                workspaceManager.ReplaceWith(WorkspaceStatus(concurrentWorkspaceId));
                return ValueTask.FromResult(SamplingResult(_suggestedName));
            },
            workspaceManager);
#pragma warning restore MCP9005

        Assert.AreEqual("2026-07-28", harness.Client.NegotiatedProtocolVersion);
        var listedTools = await harness.Client.ListToolsAsync(
            new ListToolsRequestParams(),
            CancellationToken.None);
        var scaffoldTool = listedTools.Tools.Single(static tool =>
            tool.Name == "scaffold_test_preview");
        var properties = scaffoldTool.InputSchema.GetProperty("properties");
        Assert.IsFalse(properties.TryGetProperty("requestContext", out _),
            "The SDK-injected RequestContext must not leak into the public input schema.");
        Assert.IsFalse(properties.TryGetProperty("gate", out _),
            "The DI-injected workspace gate must not leak into the public input schema.");
        Assert.IsFalse(properties.TryGetProperty("scaffoldingService", out _),
            "The DI-injected scaffolding service must not leak into the public input schema.");
        Assert.IsTrue(properties.TryGetProperty("workspaceId", out _));
        Assert.IsTrue(properties.TryGetProperty("useSampling", out _));

        var prior = harness.RawServerMessages.Count;
        var result = await harness.Client.CallToolAsync(
            "scaffold_test_preview",
            new Dictionary<string, object?>
            {
                ["testProjectName"] = "Sample.Tests",
                ["targetTypeName"] = "CacheService",
                ["targetMethodName"] = "Load",
                ["useSampling"] = true,
            },
            cancellationToken: CancellationToken.None);

        Assert.IsFalse(result.IsError is true);
        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, _suggestedName);
        Assert.AreEqual(1, samplingCount,
            "The client must satisfy exactly one sampling input request across the MRTR round trip.");
        Assert.AreEqual(2, scaffoldingService.AttemptCount,
            "MRTR retries the complete tools/call after the initial input-required result.");
        Assert.AreEqual(2, scaffoldingService.ProviderAttemptCount,
            "Each replay enters the production provider boundary once: request, then response consumption.");
        Assert.AreEqual(1, scaffoldingService.CompletedCount,
            "Only the retry carrying inputResponses may complete and return a preview.");
        Assert.AreEqual(originalWorkspaceId, scaffoldingService.LastWorkspaceId,
            "The production sampling retry must retain the workspace selected before input_required.");
        Assert.AreEqual(concurrentWorkspaceId, workspaceManager.ListWorkspaces().Single().WorkspaceId,
            "The fixture must genuinely change ambient workspace state before the retry.");
        Assert.IsNotNull(scaffoldingService.LastRequest);
        Assert.IsTrue(scaffoldingService.LastRequest.UseSampling);

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(2, results);
        var requestState = results[0].GetProperty("requestState").GetString();
        Assert.IsTrue(RequestStateCodec.TryRestoreWorkspaceId(requestState, out var restored));
        Assert.AreEqual(originalWorkspaceId, restored);
        var finalPayload = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;
        Assert.AreEqual("request-state",
            finalPayload.GetProperty("_meta").GetProperty("autoResolution").GetString());
        var inputRequired = results[0];
        Assert.AreEqual("input_required", inputRequired.GetProperty("resultType").GetString());
        var inputRequest = inputRequired
            .GetProperty("inputRequests")
            .GetProperty(RequestScopedInputAdapter.SamplingInputRequestKey);
        Assert.AreEqual(_samplingMethod, inputRequest.GetProperty("method").GetString());
        StringAssert.Contains(inputRequest.GetRawText(), "Given/When/Then");

        var terminalResult = results[1];
        Assert.AreEqual("complete", terminalResult.GetProperty("resultType").GetString(),
            "The retry must terminate as a complete CallToolResult, not another input-required result.");
        Assert.IsFalse(
            terminalResult.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        StringAssert.Contains(
            terminalResult.GetProperty("content")[0].GetProperty("text").GetString()!,
            _suggestedName);
        Assert.IsFalse(AnyServerRequest(
            harness.RawServerMessages,
            prior,
            _samplingMethod),
            "MRTR sampling must not also issue a deprecated nested sampling request.");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task LegacySession_SamplingUsesDeterministicFallback_WithoutNestedRequest()
    {
        var samplingCount = 0;
#pragma warning disable MCP9005 // Capability/handler fixture proves the provider still refuses legacy nested sampling.
        await using var harness = await CreateHarnessAsync(
            protocolVersion: "2025-11-25",
            samplingHandler: (_, _, _) =>
            {
                Interlocked.Increment(ref samplingCount);
                return ValueTask.FromResult(SamplingResult(_suggestedName));
            });
#pragma warning restore MCP9005
        var prior = harness.RawServerMessages.Count;

        var result = await harness.Client.CallToolAsync(
            "scaffold_test_preview",
            new Dictionary<string, object?> { ["useSampling"] = true },
            cancellationToken: CancellationToken.None);

        Assert.AreEqual("2025-11-25", harness.Client.NegotiatedProtocolVersion);
        var text = ((TextContentBlock)result.Content![0]).Text;
        StringAssert.Contains(text, "deterministic placeholder");
        Assert.IsFalse(AnyServerRequest(
            harness.RawServerMessages,
            prior,
            _samplingMethod));
        Assert.AreEqual(0, samplingCount);
        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ModernSession_SamplingUnsupported_UsesDeterministicFallback()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            samplingHandler: null);

        var result = await harness.Client.CallToolAsync(
            "scaffold_test_preview",
            new Dictionary<string, object?> { ["useSampling"] = true },
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, "deterministic placeholder");
    }

    [TestMethod]
    [DataRow("2025-11-25", true)]
    [DataRow(null, false)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task CraftedSamplingResponse_WithoutModernSamplingCapability_IsIgnored(
        string? protocolVersion,
        bool advertiseSampling)
    {
        var samplingCount = 0;
#pragma warning disable MCP9005 // Capability fixture plus a valid sampling result carried by the crafted retry.
        Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>?, CancellationToken,
            ValueTask<CreateMessageResult>>? handler = advertiseSampling
            ? (_, _, _) =>
            {
                Interlocked.Increment(ref samplingCount);
                return ValueTask.FromResult(SamplingResult(_suggestedName));
            }
        : null;
        await using var harness = await CreateHarnessAsync(protocolVersion, handler);
        var rawInputResponse = InputResponse
            .FromSamplingResult(SamplingResult(_suggestedName))
            .RawValue;
#pragma warning restore MCP9005
        var prior = harness.RawServerMessages.Count;

        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = "scaffold_test_preview",
                    ["arguments"] = new JsonObject { ["useSampling"] = true },
                    ["inputResponses"] = new JsonObject
                    {
                        [RequestScopedInputAdapter.SamplingInputRequestKey] =
                            JsonNode.Parse(rawInputResponse.GetRawText()),
                    },
                },
            },
            CancellationToken.None);

        var result = JsonSerializer.SerializeToElement(response.Result);
        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        StringAssert.Contains(text, "deterministic placeholder");
        Assert.IsFalse(text.Contains(_suggestedName, StringComparison.Ordinal),
            "An unsupported request must not consume a hand-crafted sampling response.");
        Assert.AreEqual(0, samplingCount);
        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MalformedSamplingResponse_IsSanitizedFallback_NotAnotherInputRequest()
    {
#pragma warning disable MCP9005 // Handler presence advertises sampling; the hand-crafted retry bypasses it.
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            samplingHandler: (_, _, _) => ValueTask.FromResult(SamplingResult(_suggestedName)));
#pragma warning restore MCP9005
        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = "scaffold_test_preview",
                    ["arguments"] = new JsonObject { ["useSampling"] = true },
                    ["inputResponses"] = new JsonObject
                    {
                        [RequestScopedInputAdapter.SamplingInputRequestKey] = JsonValue.Create(42),
                    },
                },
            },
            CancellationToken.None);

        var result = JsonSerializer.SerializeToElement(response.Result);
        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        StringAssert.Contains(text, "malformed");
        Assert.IsFalse(text.Contains("JsonException", StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task NullSamplingResponse_IsSanitizedFallback_NotNullReferenceFailure()
    {
#pragma warning disable MCP9005 // Handler presence advertises sampling; the hand-crafted retry bypasses it.
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            samplingHandler: (_, _, _) => ValueTask.FromResult(SamplingResult(_suggestedName)));
#pragma warning restore MCP9005
        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = "scaffold_test_preview",
                    ["arguments"] = new JsonObject { ["useSampling"] = true },
                    ["inputResponses"] = new JsonObject
                    {
                        [RequestScopedInputAdapter.SamplingInputRequestKey] = null,
                    },
                },
            },
            CancellationToken.None);

        var result = JsonSerializer.SerializeToElement(response.Result);
        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        StringAssert.Contains(text, "malformed");
        Assert.IsFalse(text.Contains(nameof(NullReferenceException), StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("""{"content":null,"model":"synthetic-test-model","role":"assistant"}""")]
    [DataRow("""{"content":[],"model":"synthetic-test-model","role":"assistant"}""")]
    [DataRow("""{"content":[{"type":"text","text":"   "}],"model":"synthetic-test-model","role":"assistant"}""")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task SamplingResponse_WithoutUsableText_IsSanitizedMalformedFallback(string responseJson)
    {
#pragma warning disable MCP9005 // Handler presence advertises sampling; the hand-crafted retry bypasses it.
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            samplingHandler: (_, _, _) => ValueTask.FromResult(SamplingResult(_suggestedName)));
#pragma warning restore MCP9005
        var prior = harness.RawServerMessages.Count;

        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = "scaffold_test_preview",
                    ["arguments"] = new JsonObject { ["useSampling"] = true },
                    ["inputResponses"] = new JsonObject
                    {
                        [RequestScopedInputAdapter.SamplingInputRequestKey] = JsonNode.Parse(responseJson),
                    },
                },
            },
            CancellationToken.None);

        var result = JsonSerializer.SerializeToElement(response.Result);
        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType) &&
                       resultType.GetString() == "input_required");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        StringAssert.Contains(text, "malformed");
        Assert.IsFalse(text.Contains(nameof(NullReferenceException), StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("InternalError", StringComparison.Ordinal));
        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ProviderFailure_ReportsSanitizedDiagnostic_AndDoesNotLeakSecret()
    {
        const string sentinel = "sampling-secret-sentinel";
        const string secretPath = "C:/private/tenant/input.txt";
        var reporter = new RecordingReporter();
        await using var harness = await CreateHarnessAsync(
            protocolVersion: "2025-11-25",
            samplingHandler: null,
            reporter);
        var context = CreateRequestContext(harness.Server);
        var provider = new ScaffoldingTools.McpSamplingTestNameSuggestionProvider(
            context,
            (_, _, _) => throw new InvalidOperationException($"{sentinel} at {secretPath}"));

        var result = await provider.SuggestTestNameAsync(SuggestionContext(), CancellationToken.None);

        Assert.IsNull(result.MethodName);
        Assert.IsNotNull(result.Warning);
        Assert.IsFalse(result.Warning.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(result.Warning.Contains(secretPath, StringComparison.Ordinal));
        Assert.IsFalse(result.Warning.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        StringAssert.Contains(result.Warning, "correlationId=sampling-test");
        var diagnostic = JsonSerializer.Serialize(reporter.LastReport);
        Assert.IsFalse(diagnostic.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(diagnostic.Contains(secretPath, StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ProviderCancellation_PropagatesUnchanged()
    {
        await using var harness = await CreateHarnessAsync("2025-11-25", samplingHandler: null);
        var provider = new ScaffoldingTools.McpSamplingTestNameSuggestionProvider(
            CreateRequestContext(harness.Server),
            (_, _, _) => throw new OperationCanceledException("sampling cancelled"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await provider.SuggestTestNameAsync(
                SuggestionContext(),
                CancellationToken.None));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var retryProvider = new ScaffoldingTools.McpSamplingTestNameSuggestionProvider(
            CreateRequestContext(harness.Server),
            (_, _, _) => (RequestScopedInputOutcome.Accepted, _suggestedName));
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await retryProvider.SuggestTestNameAsync(SuggestionContext(), cts.Token));

        using var responseCts = new CancellationTokenSource();
        var responseReached = false;
        var responseProvider = new ScaffoldingTools.McpSamplingTestNameSuggestionProvider(
            CreateRequestContext(harness.Server),
            (_, _, _) =>
            {
                responseReached = true;
                responseCts.Cancel();
                return (RequestScopedInputOutcome.Accepted, _suggestedName);
            });
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await responseProvider.SuggestTestNameAsync(
                SuggestionContext(),
                responseCts.Token));
        Assert.IsTrue(responseReached,
            "Cancellation must be observed after response consumption, not only before sampling starts.");
    }

#pragma warning disable MCP9005 // Test fixture construction for the request-scoped sampling response.
    private static CreateMessageResult SamplingResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        Model = "synthetic-test-model",
        Role = Role.Assistant,
        StopReason = "endTurn",
    };
#pragma warning restore MCP9005

    private static ScaffoldTestNameSuggestionContext SuggestionContext() => new(
        "CacheService",
        "Load",
        "Task<string> Load(string key)",
        "Sample",
        ["Load_WhenFound_ReturnsValue"]);

    private static RequestContext<CallToolRequestParams> CreateRequestContext(McpServer server) =>
        new(
            server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall },
            new CallToolRequestParams { Name = "scaffold_test_preview" });

#pragma warning disable MCP9005 // The nullable delegate is the SDK client sampling-handler contract under test.
    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocolVersion,
        Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>?, CancellationToken,
            ValueTask<CreateMessageResult>>? samplingHandler,
        IUnexpectedExceptionReporter? reporter = null)
#pragma warning restore MCP9005
    {
        var services = new ServiceCollection();
        if (reporter is not null)
        {
            services.AddSingleton(reporter);
            services.AddSingleton<IUnexpectedExceptionReporter>(reporter);
        }

        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "sampling-mrtr-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticSamplingTools>();
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

#pragma warning disable MCP9005 // Setting SamplingHandler is required to advertise the client capability in this fixture.
        var handlers = samplingHandler is null
            ? new McpClientHandlers()
            : new McpClientHandlers { SamplingHandler = samplingHandler };
#pragma warning restore MCP9005
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"sampling-mrtr-{protocolVersion ?? "modern"}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: handlers,
            disposalFailureContext: "sampling-mrtr-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

#pragma warning disable MCP9005 // The SDK client handler advertises sampling for this production-tool MRTR fixture.
    private static async Task<InMemoryMcpClientServerHarness> CreateProductionHarnessAsync(
        RecordingScaffoldingService scaffoldingService,
        Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>?, CancellationToken,
            ValueTask<CreateMessageResult>> samplingHandler,
        IWorkspaceManager workspaceManager)
#pragma warning restore MCP9005
    {
        var selection = ToolTierSelection.All;
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceExecutionGate, PassThroughWorkspaceExecutionGate>();
        services.AddSingleton<IScaffoldingService>(scaffoldingService);
        services.AddSingleton(workspaceManager);
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "sampling-production-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithToolsFromAssembly(typeof(ScaffoldingTools).Assembly)
            .WithResourcesFromAssembly(typeof(ScaffoldingTools).Assembly)
            .WithPromptsFromAssembly(typeof(ScaffoldingTools).Assembly)
            .WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(StaticListResultFilter.CreateTools);
                filters.AddCallToolFilter(StructuredCallToolFilter.Create);
            });
        services.AddRoslynMcpSurfaceRegistrationPolicy(selection);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
#pragma warning disable MCP9005 // Setting SamplingHandler advertises the client capability under test.
        var handlers = new McpClientHandlers { SamplingHandler = samplingHandler };
#pragma warning restore MCP9005
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "sampling-production-mrtr-modern",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: handlers,
            disposalFailureContext: "sampling-production-mrtr-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: null,
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
    private sealed class SyntheticSamplingTools
    {
        [McpServerTool(Name = "scaffold_test_preview")]
        public static async Task<string> Preview(
            RequestContext<CallToolRequestParams> requestContext,
            bool useSampling = false,
            CancellationToken cancellationToken = default)
        {
            if (!useSampling)
            {
                return JsonSerializer.Serialize(new { methodName = "Load_Needs_Test" });
            }

            var provider = new ScaffoldingTools.McpSamplingTestNameSuggestionProvider(requestContext);
            var result = await provider.SuggestTestNameAsync(
                SuggestionContext(),
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                methodName = result.MethodName ?? "Load_Needs_Test",
                warning = result.Warning,
            });
        }
    }

    private sealed class RecordingReporter : IUnexpectedExceptionReporter
    {
        public UnexpectedExceptionDetails? LastReport { get; private set; }

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            LastReport = PublicExceptionDetailPolicy.ProjectUnexpected(exception, "sampling-test");
            return LastReport;
        }
    }

    private sealed class RecordingScaffoldingService : IScaffoldingService
    {
        private int _attemptCount;
        private int _providerAttemptCount;
        private int _completedCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);
        public int ProviderAttemptCount => Volatile.Read(ref _providerAttemptCount);
        public int CompletedCount => Volatile.Read(ref _completedCount);
        public string? LastWorkspaceId { get; private set; }
        public ScaffoldTestDto? LastRequest { get; private set; }

        public Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(
            string workspaceId,
            ScaffoldTypeDto request,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public async Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(
            string workspaceId,
            ScaffoldTestDto request,
            CancellationToken ct,
            ITestNameSuggestionProvider? testNameSuggestionProvider = null)
        {
            Interlocked.Increment(ref _attemptCount);
            LastWorkspaceId = workspaceId;
            LastRequest = request;
            var provider = testNameSuggestionProvider
                ?? throw new InvalidOperationException(
                    "The production tool did not supply its request-scoped sampling provider.");

            Interlocked.Increment(ref _providerAttemptCount);
            var suggestion = await provider
                .SuggestTestNameAsync(SuggestionContext(), ct)
                .ConfigureAwait(false);
            Interlocked.Increment(ref _completedCount);

            return new RefactoringPreviewDto(
                "production-preview-token",
                $"Scaffolded {suggestion.MethodName}",
                [],
                suggestion.Warning is null ? null : [suggestion.Warning]);
        }

        public Task<RefactoringPreviewDto> PreviewScaffoldTestBatchAsync(
            string workspaceId,
            ScaffoldTestBatchDto request,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewScaffoldFirstTestFileAsync(
            string workspaceId,
            ScaffoldFirstTestFileDto request,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class SamplingWorkspaceManager(params WorkspaceStatusDto[] workspaces) : IWorkspaceManager
    {
        private WorkspaceStatusDto[] _workspaces = workspaces;

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Volatile.Read(ref _workspaces);
        public void ReplaceWith(params WorkspaceStatusDto[] replacement) =>
            Volatile.Write(ref _workspaces, replacement);
        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId,
            string? projectName,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(
            string workspaceId,
            string filePath,
            CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) =>
            throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) =>
            throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }

    private static WorkspaceStatusDto WorkspaceStatus(string workspaceId) => new(
        WorkspaceId: workspaceId,
        LoadedPath: "C:/synthetic/sampling.slnx",
        WorkspaceVersion: 1,
        SnapshotToken: workspaceId + ":1",
        LoadedAtUtc: DateTimeOffset.UtcNow,
        ProjectCount: 1,
        DocumentCount: 1,
        Projects: Array.Empty<ProjectStatusDto>(),
        IsLoaded: true,
        IsStale: false,
        WorkspaceDiagnostics: Array.Empty<DiagnosticDto>());
}
