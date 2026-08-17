using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ProtocolVersionResultShapeWireTests
{
    private const string ToolName = "synthetic_array_result";

    [TestMethod]
    public async Task ArraySchema_ResultShapeAndMetadataMatchNegotiatedProtocol()
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

            var tools = await harness.Client.ListToolsAsync(
                new ListToolsRequestParams(),
                CancellationToken.None);
            var advertised = tools.Tools.Single(static tool => tool.Name == ToolName).OutputSchema;
            Assert.IsNotNull(advertised);
            var schema = JsonNode.Parse(advertised.Value.GetRawText());
            Assert.IsNotNull(schema);

            var priorMessageCount = harness.RawServerMessages.Count;
            _ = await harness.Client.CallToolAsync(
                ToolName,
                cancellationToken: CancellationToken.None);
            var result = FindSingleNewResult(harness.RawServerMessages, priorMessageCount);

            Assert.AreEqual("result-meta", result.GetProperty("_meta").GetProperty("sentinel").GetString());
            if (protocol.Modern)
            {
                Assert.AreEqual("complete", result.GetProperty("resultType").GetString());
            }
            else
            {
                Assert.IsFalse(result.TryGetProperty("resultType", out _));
            }

            var text = result.GetProperty("content")[0];
            Assert.AreEqual("text-meta", text.GetProperty("_meta").GetProperty("sentinel").GetString());
            var annotations = text.GetProperty("annotations");
            CollectionAssert.AreEqual(
                new[] { "assistant" },
                annotations.GetProperty("audience").EnumerateArray().Select(static item => item.GetString()).ToArray());
            Assert.AreEqual(0.75, annotations.GetProperty("priority").GetDouble(), 0.001);

            var structured = result.GetProperty("structuredContent");
            Assert.AreEqual(protocol.Modern ? JsonValueKind.Array : JsonValueKind.Object, structured.ValueKind);
            Assert.IsTrue(
                GeneratedJsonSchemaMatcher.Matches(structured, schema),
                $"Structured result does not match the {protocol.Expected} advertised schema: {structured.GetRawText()}");

            if (protocol.Modern)
            {
                CollectionAssert.AreEqual(
                    new[] { "alpha", "beta" },
                    structured.EnumerateArray().Select(static item => item.GetString()).ToArray());
            }
            else
            {
                CollectionAssert.AreEqual(
                    new[] { "alpha", "beta" },
                    structured.GetProperty("result").EnumerateArray().Select(static item => item.GetString()).ToArray());
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
                    Name = "result-shape-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticArrayResultTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"result-shape-{protocolVersion ?? "modern"}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "protocol-result-shape",
            cancellationToken: CancellationToken.None,
            protocolVersion: protocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static JsonElement FindSingleNewResult(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount)
    {
        var results = rawMessages
            .Skip(priorMessageCount)
            .Select(static rawMessage => JsonNode.Parse(rawMessage))
            .OfType<JsonObject>()
            .Where(static message => message["result"] is not null)
            .Select(static message => JsonSerializer.SerializeToElement(message["result"]))
            .ToArray();

        Assert.HasCount(1, results);
        return results[0];
    }

    [McpServerToolType]
    private sealed class SyntheticArrayResultTools
    {
        [McpServerTool(Name = ToolName, OutputSchemaType = typeof(string[]), UseStructuredContent = true)]
        public static CallToolResult GetArrayResult()
        {
            using var structuredDocument = JsonDocument.Parse("[\"alpha\",\"beta\"]");
            return new CallToolResult
            {
                Meta = new JsonObject { ["sentinel"] = "result-meta" },
                ResultType = "complete",
                StructuredContent = structuredDocument.RootElement.Clone(),
                Content =
                [
                    new TextContentBlock
                    {
                        Text = "[\"alpha\",\"beta\"]",
                        Meta = new JsonObject { ["sentinel"] = "text-meta" },
                        Annotations = new Annotations
                        {
                            Audience = [Role.Assistant],
                            Priority = 0.75F,
                        },
                    },
                ],
            };
        }
    }
}
