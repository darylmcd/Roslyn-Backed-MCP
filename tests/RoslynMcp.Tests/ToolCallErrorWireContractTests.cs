using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tool-call-error-envelope-wire-contract: raw JSON-RPC coverage for the <c>tools/call</c>
/// FAILURE envelope. The pre-existing suites cover the two halves separately and neither pins
/// the serialized failure frame: <c>StructuredCallToolFilterTests</c> calls the <c>internal</c>
/// helper <see cref="StructuredCallToolFilter.BuildErrorResult"/> in-process (never serializing
/// a frame), and <c>StructuredContentWireContractTests</c> drives the real wire but asserts
/// <c>IsError is false</c> for every case. This file drives an unexpected, nested exception
/// through the public client transport and asserts where the envelope lands (JSON-RPC
/// <c>result</c>, never a protocol <c>error</c>), that <c>isError</c> survives serialization,
/// that the application <c>_meta</c> is not promoted onto the protocol result <c>_meta</c>, the
/// era discriminator behavior, and that nothing sensitive leaks.
/// </summary>
[TestClass]
public sealed class ToolCallErrorWireContractTests
{
    private const string ToolName = "synthetic_unexpected_failure";

    /// <summary>Never allowed to appear anywhere in the serialized frame.</summary>
    private const string SecretSentinel = "SECRET-SENTINEL-9f13c2";

    private const string LocalPathFragment = @"C:\local\path\to\redacted";

    private const string InnerMessage = "inner detail " + SecretSentinel + " at " + LocalPathFragment;

    [TestMethod]
    public async Task UnexpectedToolException_SerializesAsRedactedIsErrorResult()
    {
        var protocols = new (string? Requested, string Expected, bool Modern)[]
        {
            ("2025-11-25", "2025-11-25", false),
            (null, "2026-07-28", true),
        };

        foreach (var protocol in protocols)
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            Assert.AreEqual(protocol.Expected, harness.Client.NegotiatedProtocolVersion);

            var priorMessageCount = harness.RawServerMessages.Count;
            _ = await harness.Client.CallToolAsync(
                ToolName,
                cancellationToken: CancellationToken.None);

            var frame = FindSingleNewResponseFrame(harness.RawServerMessages, priorMessageCount);
            var rawFrame = frame.ToJsonString();

            // (1) The failure lands in the JSON-RPC `result` member, never the protocol `error`.
            Assert.IsNull(
                frame["error"],
                $"tools/call failures must stay application-level, not protocol errors: {rawFrame}");
            var result = frame["result"] as JsonObject;
            Assert.IsNotNull(result, $"Response frame carried no result object: {rawFrame}");

            // (2) isError survives serialization.
            Assert.AreEqual(
                true,
                result["isError"]?.GetValue<bool>(),
                $"Serialized failure frame must set isError: {rawFrame}");

            // (3) Exactly one text content block.
            var content = result["content"] as JsonArray;
            Assert.IsNotNull(content, $"Failure frame carried no content array: {rawFrame}");
            Assert.HasCount(1, content);
            var block = (JsonObject)content[0]!;
            Assert.AreEqual("text", block["type"]?.GetValue<string>());

            // (4) The text block parses as the structured application error envelope.
            var payload = JsonNode.Parse(block["text"]!.GetValue<string>()) as JsonObject;
            Assert.IsNotNull(payload, $"Failure text block was not a JSON object: {rawFrame}");
            Assert.AreEqual(true, payload["error"]?.GetValue<bool>());
            Assert.AreEqual("InternalError", payload["category"]?.GetValue<string>());
            Assert.AreEqual(ToolName, payload["tool"]?.GetValue<string>());
            var correlationId = payload["correlationId"]?.GetValue<string>();
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(correlationId),
                "InternalError envelopes must carry a safe operator correlation reference.");
            Assert.IsInstanceOfType<JsonObject>(
                payload["_meta"],
                "The application error envelope must carry the gate-metrics _meta block.");

            // (5) schemaHint is InvalidArgument-only; an unexpected InternalError must not carry
            // it, nor the exceptionType/stackTrace fields the generic envelope path would add.
            Assert.IsNull(payload["schemaHint"], $"schemaHint leaked onto an InternalError: {rawFrame}");
            Assert.IsNull(payload["exceptionType"], $"exceptionType leaked onto an InternalError: {rawFrame}");
            Assert.IsNull(payload["stackTrace"], $"stackTrace leaked onto an InternalError: {rawFrame}");

            // (6) The application _meta / schemaHint must not be promoted to the protocol result _meta.
            if (result["_meta"] is JsonObject protocolMeta)
            {
                Assert.IsNull(protocolMeta["schemaHint"], rawFrame);
                Assert.IsNull(protocolMeta["correlationId"], rawFrame);
                Assert.IsNull(protocolMeta["category"], rawFrame);
            }

            // (7) Era discriminator: failure frames are era-shaped exactly like success frames —
            // the modern era carries `resultType: "complete"`, the legacy era must omit a field
            // that protocol revision does not define. Before this suite existed the error path
            // bypassed the result shaper entirely and leaked the discriminator into legacy
            // sessions; StructuredCallToolFilter now routes BuildErrorResult through
            // ApplyProtocolResultShape.
            if (protocol.Modern)
            {
                Assert.AreEqual(
                    "complete",
                    result["resultType"]?.GetValue<string>(),
                    $"Modern-era failure frames must carry the era discriminator: {rawFrame}");
            }
            else
            {
                Assert.IsNull(
                    result["resultType"],
                    $"Legacy-era failure frames must not carry the era discriminator: {rawFrame}");
            }

            // (8) Nothing sensitive survives to the wire.
            foreach (var forbidden in new[]
            {
                SecretSentinel,
                InnerMessage,
                LocalPathFragment,
                nameof(NullReferenceException),
                nameof(InvalidOperationException),
                "at RoslynMcp.",
                "stackTrace",
            })
            {
                Assert.IsFalse(
                    rawFrame.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Serialized failure frame leaked '{forbidden}': {rawFrame}");
            }
        }
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(string? protocolVersion)
    {
        var services = new ServiceCollection();
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "tool-call-error-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticUnexpectedFailureTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"tool-call-error-{protocolVersion ?? "modern"}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "tool-call-error-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: protocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    /// <summary>
    /// Pulls the single response frame emitted since <paramref name="priorMessageCount"/>. Unlike
    /// the success-path helper in <c>ProtocolVersionResultShapeWireTests</c> this also matches
    /// frames carrying a JSON-RPC <c>error</c> member, so the test can prove that member is absent
    /// rather than silently failing to locate any frame at all.
    /// </summary>
    private static JsonObject FindSingleNewResponseFrame(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount)
    {
        var frames = rawMessages
            .Skip(priorMessageCount)
            .Select(static rawMessage => JsonNode.Parse(rawMessage))
            .OfType<JsonObject>()
            .Where(static message => message["result"] is not null || message["error"] is not null)
            .ToArray();

        Assert.HasCount(1, frames);
        return frames[0];
    }

    [McpServerToolType]
    private sealed class SyntheticUnexpectedFailureTools
    {
        /// <summary>
        /// Throws an unclassified, nested exception. <see cref="NullReferenceException"/> has no
        /// registered handler in <c>ToolErrorHandler</c> and is not binding-like, so it routes to
        /// the terminal <c>InternalError</c> branch — the redaction path under test. Deliberately
        /// not <see cref="InvalidOperationException"/>, which IS registered and would classify as
        /// <c>InvalidOperation</c> instead.
        /// </summary>
        [McpServerTool(Name = ToolName)]
        public static string Fail() =>
            throw new NullReferenceException(
                "outer " + SecretSentinel,
                new InvalidOperationException(InnerMessage));
    }
}
