using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Prompts;
using RoslynMcp.Tests.Helpers;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace RoslynMcp.Tests;

/// <summary>
/// Focused contract tests for the <c>prompts/get</c> error boundary
/// (<see cref="GetPromptErrorFilter"/>) and the sanitized legacy
/// <see cref="PromptMessageBuilder.CreateErrorMessage"/> body. Wire tests stand up a real
/// in-proc client/server pair so the assertions run against the serialized JSON-RPC payloads a
/// client actually receives.
/// </summary>
[TestClass]
public sealed class PromptCallErrorFilterTests
{
    private const string _secretSentinel =
        "SECRET-SENTINEL-Server=db.internal;Password=hunter2;C:/private/source.cs";

    private const int _internalErrorCode = -32603;
    private const int _invalidParamsCode = -32602;

    [TestMethod]
    public async Task UnexpectedPromptFailure_RidesJsonRpcErrorChannelWithSanitizedPayload()
    {
        var sink = new CapturingSink();
        await using var harness = await CreateHarnessAsync("prompt-error-filter-wire", sink);

        var priorMessageCount = harness.RawServerMessages.Count;
        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.GetPromptAsync(
            "throwing_prompt",
            new Dictionary<string, object?> { ["target"] = "anything" },
            cancellationToken: CancellationToken.None));

        var (error, rawMessages) = FindSingleNewError(harness.RawServerMessages, priorMessageCount);
        Assert.AreEqual(_internalErrorCode, error.GetProperty("code").GetInt32());
        StringAssert.Contains(
            error.GetProperty("message").GetString(),
            "correlationId",
            StringComparison.Ordinal);

        // No successful prompts/get result may exist for this request — a "prompt message
        // describing the failure" is indistinguishable from real prompt content.
        foreach (var rawMessage in rawMessages)
        {
            using var document = JsonDocument.Parse(rawMessage);
            if (document.RootElement.TryGetProperty("result", out var result))
            {
                Assert.IsFalse(
                    result.TryGetProperty("messages", out _),
                    "A prompt failure must not produce a successful prompts/get result.");
            }
        }

        // The serialized wire payload must not disclose exception internals.
        var serializedResponses = string.Join('\n', rawMessages);
        Assert.IsFalse(serializedResponses.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(serializedResponses.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.IsFalse(serializedResponses.Contains("IOException", StringComparison.Ordinal));
        Assert.IsFalse(serializedResponses.Contains("   at ", StringComparison.Ordinal));
        Assert.IsFalse(serializedResponses.Contains("private/source.cs", StringComparison.Ordinal));

        // Server-side observability retains the secret-safe structure under the GetPrompt category.
        Assert.HasCount(1, sink.Events);
        var diagnosticEvent = sink.Events.Single();
        Assert.AreEqual("GetPrompt", diagnosticEvent.Category);
        Assert.AreEqual("UnexpectedFailure", diagnosticEvent.EventName);
        Assert.IsTrue(diagnosticEvent.Exception.ExceptionTypes.Any(
            static type => type.EndsWith(nameof(InvalidOperationException), StringComparison.Ordinal)));
        Assert.IsTrue(diagnosticEvent.Exception.ExceptionTypes.Any(
            static type => type.EndsWith(nameof(IOException), StringComparison.Ordinal)));
        var serializedEvent = JsonSerializer.Serialize(diagnosticEvent);
        Assert.IsFalse(serializedEvent.Contains(_secretSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MissingRequiredParameter_KeepsInvalidParamsContract()
    {
        var sink = new CapturingSink();
        await using var harness = await CreateHarnessAsync("prompt-invalid-params-wire", sink);

        var priorMessageCount = harness.RawServerMessages.Count;
        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.GetPromptAsync(
            "throwing_prompt",
            cancellationToken: CancellationToken.None));

        var (error, rawMessages) = FindSingleNewError(harness.RawServerMessages, priorMessageCount);
        Assert.AreEqual(_invalidParamsCode, error.GetProperty("code").GetInt32());
        var serializedResponses = string.Join('\n', rawMessages);
        Assert.IsFalse(serializedResponses.Contains(_secretSentinel, StringComparison.Ordinal));

        // Parameter validation is an expected failure — never reported as unexpected.
        Assert.IsEmpty(sink.Events);
    }

    [TestMethod]
    public async Task UnknownPromptName_KeepsSdkProtocolErrorUntouched()
    {
        var sink = new CapturingSink();
        await using var harness = await CreateHarnessAsync("prompt-unknown-name-wire", sink);

        var priorMessageCount = harness.RawServerMessages.Count;
        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.GetPromptAsync(
            "no_such_prompt",
            cancellationToken: CancellationToken.None));

        var (error, _) = FindSingleNewError(harness.RawServerMessages, priorMessageCount);
        Assert.AreEqual(_invalidParamsCode, error.GetProperty("code").GetInt32());
        Assert.IsEmpty(sink.Events);
    }

    [TestMethod]
    public async Task CancelledPromptCall_PropagatesCancellationUntouchedByTheBoundary()
    {
        var sink = new CapturingSink();
        await using var harness = await CreateHarnessAsync("prompt-cancellation-wire", sink);

        var priorMessageCount = harness.RawServerMessages.Count;
        await Assert.ThrowsAsync<McpException>(async () => await harness.Client.GetPromptAsync(
            "cancelling_prompt",
            cancellationToken: CancellationToken.None));

        // The boundary must rethrow OperationCanceledException untouched: no unexpected-failure
        // report may have been made, and the resulting SDK error must not carry the filter's
        // sanitized InternalError envelope (its "correlationId" marker).
        Assert.IsEmpty(sink.Events);
        var (error, _) = FindSingleNewError(harness.RawServerMessages, priorMessageCount);
        Assert.IsFalse(
            error.GetProperty("message").GetString()!.Contains("correlationId", StringComparison.Ordinal),
            "Cancellation must not be converted into the boundary's sanitized InternalError.");
    }

    [TestMethod]
    public void TranslateException_ArgumentExceptionKeepsActionableInvalidParams()
    {
        var translated = GetPromptErrorFilter.TranslateException(
            new ArgumentException("Missing required parameter 'target'.", "target"),
            "throwing_prompt",
            reporter: null,
            logger: null);

        Assert.AreEqual(McpErrorCode.InvalidParams, translated.ErrorCode);
        StringAssert.Contains(translated.Message, "throwing_prompt", StringComparison.Ordinal);
        StringAssert.Contains(translated.Message, "target", StringComparison.Ordinal);
    }

    [TestMethod]
    public void TranslateException_JsonExceptionNeverEchoesRawMessage()
    {
        var translated = GetPromptErrorFilter.TranslateException(
            new JsonException(_secretSentinel),
            "throwing_prompt",
            reporter: null,
            logger: null);

        Assert.AreEqual(McpErrorCode.InvalidParams, translated.ErrorCode);
        Assert.IsFalse(translated.Message.Contains(_secretSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void TranslateException_UnexpectedExceptionReportsGetPromptCategoryAndSanitizes()
    {
        var sink = new CapturingSink();
        var reporter = new ServerObservabilityReporter(sink);
        McpProtocolException translated;
        string correlationId;

        using (RequestCorrelationContext.Begin())
        {
            correlationId = RequestCorrelationContext.Current!;
            translated = GetPromptErrorFilter.TranslateException(
                new InvalidOperationException(_secretSentinel, new IOException(_secretSentinel)),
                "throwing_prompt",
                reporter,
                logger: null);
        }

        Assert.AreEqual(McpErrorCode.InternalError, translated.ErrorCode);
        StringAssert.Contains(translated.Message, correlationId, StringComparison.Ordinal);
        Assert.IsFalse(translated.Message.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(translated.Message.Contains(
            nameof(InvalidOperationException), StringComparison.Ordinal));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual("GetPrompt", sink.Events.Single().Category);
        Assert.AreEqual(correlationId, sink.Events.Single().Exception.CorrelationId);
    }

    [TestMethod]
    public void CreateErrorMessage_EmitsCorrelationAndRemediationNeverExceptionMessage()
    {
        string correlationId;
        PromptMessage message;

        using (RequestCorrelationContext.Begin())
        {
            correlationId = RequestCorrelationContext.Current!;
            message = PromptMessageBuilder.CreateErrorMessage(
                "explain_error",
                new InvalidOperationException(_secretSentinel, new IOException(_secretSentinel)));
        }

        var text = Assert.IsInstanceOfType<TextContentBlock>(message.Content).Text;
        StringAssert.Contains(text, "explain_error", StringComparison.Ordinal);
        StringAssert.Contains(text, correlationId, StringComparison.Ordinal);
        StringAssert.Contains(text, "Retry once", StringComparison.Ordinal);
        Assert.IsFalse(text.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("private/source.cs", StringComparison.Ordinal));
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string transportName,
        CapturingSink sink)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "roslyn-mcp-test", Version = "1.0.0" };
            })
            .WithPrompts<FilterTestPrompts>()
            .WithRequestFilters(filters =>
                filters.AddGetPromptFilter(GetPromptErrorFilter.Create));
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName,
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: transportName,
            cancellationToken: CancellationToken.None,
            protocolVersion: null,
            serverOptions: options,
            serverServicesFactory: () => new ServiceCollection()
                .AddSingleton<IUnexpectedExceptionReporter>(new ServerObservabilityReporter(sink))
                .BuildServiceProvider(),
            captureServerMessages: true);
    }

    private static (JsonElement Error, IReadOnlyList<string> RawMessages) FindSingleNewError(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount)
    {
        var newMessages = rawMessages.Skip(priorMessageCount).ToArray();
        var errors = new List<JsonElement>();
        foreach (var rawMessage in newMessages)
        {
            using var document = JsonDocument.Parse(rawMessage);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                errors.Add(error.Clone());
            }
        }

        Assert.HasCount(1, errors, "Expected exactly one JSON-RPC error response.");
        return (errors[0], newMessages);
    }

    /// <summary>
    /// Test-only prompt surface. The production prompt handlers all catch their own exceptions
    /// (until the <c>prompt-error-catch-retirement-*</c> rows land), so exercising the boundary
    /// requires prompts whose failures actually reach the filter.
    /// </summary>
    [McpServerPromptType]
    public sealed class FilterTestPrompts
    {
        [McpServerPrompt(Name = "throwing_prompt")]
        [Description("Test prompt whose handler throws a nested secret-bearing exception.")]
        public static IEnumerable<PromptMessage> ThrowingPrompt(
            [Description("Required parameter used for the InvalidParams contract test.")] string target) =>
            throw new InvalidOperationException(
                _secretSentinel,
                new IOException(_secretSentinel));

        [McpServerPrompt(Name = "cancelling_prompt")]
        [Description("Test prompt whose handler surfaces a cooperative cancellation signal.")]
        public static IEnumerable<PromptMessage> CancellingPrompt() =>
            throw new OperationCanceledException("cooperative cancellation");
    }

    private sealed class CapturingSink : IServerObservabilitySink
    {
        public List<ServerObservabilityEvent> Events { get; } = [];
        public bool IsEnabled => true;

        public void Write(ServerObservabilityEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }
}
