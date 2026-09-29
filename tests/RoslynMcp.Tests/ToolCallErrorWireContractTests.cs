using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
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
// donotparallelize-audit-wave-30: [DoNotParallelize] removed. Neither test touches the assembly-shared
// WorkspaceManager: each harness builds its own ServiceCollection/McpServer over the in-memory
// transport, WorkspaceFastFails loads two fresh SampleSolution copies into a class-private
// WorkspaceManager (closed in finally), and its sanctioned root is a per-harness SecurityOptions
// singleton — never the process-global SecurityOptionsSnapshot. Every path it writes is a
// Guid-named directory under TestTempRoot.Current, so SolutionDiscoveryHelper's static root-scan
// cache only ever sees keys unique to this class. No env-var, static, or child-process mutation.
// Validated by a bounded repeated (3x) concurrent run alongside its wave-30 sibling and
// parallel-enabled workspace-loading classes, green every time.
[TestClass]
public sealed class ToolCallErrorWireContractTests : IsolatedWorkspaceTestBase
{
    private const string ToolName = "synthetic_unexpected_failure";

    /// <summary>Never allowed to appear anywhere in the serialized frame.</summary>
    private const string SecretSentinel = "SECRET-SENTINEL-9f13c2";

    private const string LocalPathFragment = @"C:\local\path\to\redacted";

    private const string InnerMessage = "inner detail " + SecretSentinel + " at " + LocalPathFragment;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

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
            Assert.AreNotEqual("unavailable", correlationId,
                "A generated wire correlation id must not degrade to the literal fallback sentinel.");
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
                // Compare against the JSON-ESCAPED form, not the raw literal. rawFrame is
                // serialized JSON, so a backslash in the sentinel (InnerMessage and
                // LocalPathFragment both carry the local path) is written escaped -- a raw
                // Contains for the single-backslash literal could never match even when the
                // path really leaked, which made those two entries decorative rather than
                // load-bearing. JsonEncodedText.Encode applies the same escaping the
                // serializer did, so a real leak now provably fails this assertion.
                var escaped = JsonEncodedText.Encode(forbidden).ToString();

                Assert.IsFalse(
                    rawFrame.Contains(escaped, StringComparison.OrdinalIgnoreCase),
                    $"Serialized failure frame leaked '{forbidden}': {rawFrame}");
            }
        }
    }

    [TestMethod]
    public async Task WorkspaceFastFails_UseEraSpecificWireShape()
    {
        var firstSolutionPath = CreateSampleSolutionCopy();
        var secondSolutionPath = CreateSampleSolutionCopy();
        var firstRoot = Path.GetDirectoryName(firstSolutionPath)!;
        var secondRoot = Path.GetDirectoryName(secondSolutionPath)!;
        var ambiguousRoot = Path.Combine(TestTempRoot.Current, "ambiguous-wire-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var manager = CreateIsolatedWorkspaceManager();
            var firstWorkspace = await manager.LoadAsync(firstSolutionPath, CancellationToken.None);
            var secondWorkspace = await manager.LoadAsync(secondSolutionPath, CancellationToken.None);

            try
            {
                foreach (var protocol in Protocols())
                {
                    await using var harness = await CreateHarnessAsync(protocol.Requested, manager);
                    var frame = await CallAndCaptureAsync(harness, "compile_check", arguments: null);
                    AssertFastFailFrame(frame, "loaded workspace", protocol.Modern);

                    var validateFrame = await CallAndCaptureAsync(harness, "validate_workspace", arguments: null);
                    AssertFastFailFrame(validateFrame, "Candidates:", protocol.Modern);
                    var payload = ErrorPayload(validateFrame);
                    StringAssert.Contains(payload["message"]!.GetValue<string>(), firstWorkspace.WorkspaceId);
                    StringAssert.Contains(payload["message"]!.GetValue<string>(), secondWorkspace.WorkspaceId);
                    StringAssert.Contains(payload["schemaHint"]!.GetValue<string>(), "workspaceId");
                }
            }
            finally
            {
                manager.Close(firstWorkspace.WorkspaceId);
                manager.Close(secondWorkspace.WorkspaceId);
            }

            Assert.IsEmpty(manager.ListWorkspaces(), "The ambiguous auto-load case must start with no ambient workspace.");
            Directory.CreateDirectory(ambiguousRoot);
            File.WriteAllText(Path.Combine(ambiguousRoot, "Alpha.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(ambiguousRoot, "Beta.slnx"), "<Solution />");
            var sourcePath = Path.Combine(ambiguousRoot, "Class1.cs");
            File.WriteAllText(sourcePath, "// source");

            var arguments = new Dictionary<string, object?>
            {
                ["filePath"] = sourcePath,
                ["line"] = 1,
                ["column"] = 1,
            };
            foreach (var protocol in Protocols())
            {
                await using var harness = await CreateHarnessAsync(protocol.Requested, manager, ambiguousRoot);
                var frame = await CallAndCaptureAsync(harness, "symbol_info", arguments);
                AssertFastFailFrame(frame, "candidate solutions", protocol.Modern);
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(firstRoot);
            TestFixtureFileSystem.DeleteDirectoryIfExists(secondRoot);
            TestFixtureFileSystem.DeleteDirectoryIfExists(ambiguousRoot);
        }
    }

    [TestMethod]
    public async Task MissingRequiredArguments_NameEveryOmittedFieldOnTheWire()
    {
        foreach (var protocol in Protocols())
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            var frame = await CallAndCaptureAsync(harness, "workspace_load", arguments: null);
            AssertFastFailFrame(frame, "path", protocol.Modern);
            var payload = ErrorPayload(frame);
            Assert.AreEqual("ArgumentException", payload["exceptionType"]?.GetValue<string>());
            StringAssert.Contains(payload["schemaHint"]!.GetValue<string>(), "path");

            var multipleFrame = await CallAndCaptureAsync(harness, "synthetic_required_arguments", arguments: null);
            var multiplePayload = ErrorPayload(multipleFrame);
            Assert.AreEqual("InvalidArgument", multiplePayload["category"]?.GetValue<string>());
            StringAssert.Contains(multiplePayload["message"]!.GetValue<string>(), "first");
            StringAssert.Contains(multiplePayload["message"]!.GetValue<string>(), "second");
        }
    }

    [TestMethod]
    public async Task PartiallySuppliedRequiredArguments_NameOnlyTheOmittedField()
    {
        foreach (var protocol in Protocols())
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            var partial = new Dictionary<string, object?> { ["first"] = "supplied" };
            var frame = await CallAndCaptureAsync(harness, "synthetic_required_arguments", partial);
            var payload = ErrorPayload(frame);
            Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>());
            StringAssert.Contains(payload["message"]!.GetValue<string>(), "'second'");
            Assert.IsFalse(payload["message"]!.GetValue<string>().Contains("'first'", StringComparison.Ordinal));

            var catalogPartial = new Dictionary<string, object?>
            {
                ["workspaceId"] = "synthetic-workspace",
                ["filePath"] = "C:/synthetic/source.cs",
                ["line"] = 1,
            };
            var catalogFrame = await CallAndCaptureAsync(harness, "symbol_info", catalogPartial);
            var catalogPayload = ErrorPayload(catalogFrame);
            StringAssert.Contains(catalogPayload["message"]!.GetValue<string>(), "'column'");
            Assert.IsFalse(catalogPayload["message"]!.GetValue<string>().Contains("'filePath'", StringComparison.Ordinal));
            StringAssert.Contains(catalogPayload["schemaHint"]!.GetValue<string>(), "column");
        }
    }

    [TestMethod]
    public async Task MalformedSuppliedValue_WithOmittedRequiredField_NamesTheOmission()
    {
        foreach (var protocol in Protocols())
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            var arguments = new Dictionary<string, object?>
            {
                ["workspaceId"] = "synthetic-workspace",
                ["filePath"] = "C:/synthetic/source.cs",
                ["line"] = "not-an-integer",
            };
            var frame = await CallAndCaptureAsync(harness, "symbol_info", arguments);
            var payload = ErrorPayload(frame);
            Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>());
            Assert.AreEqual("JsonException", payload["exceptionType"]?.GetValue<string>());
            StringAssert.Contains(payload["message"]!.GetValue<string>(), "'column'");
            StringAssert.Contains(payload["schemaHint"]!.GetValue<string>(), "column");
        }
    }

    [TestMethod]
    public async Task MrtrRetry_UsesRecoveredArgumentsToNameTheRemainingOmission()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            workspaceManager: new FailClosedWorkspaceManagerStub(),
            elicitationHandler: (_, _) => ValueTask.FromResult(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["path"] = JsonSerializer.SerializeToElement("C:/synthetic/recovered.slnx"),
                },
            }));

        var priorMessageCount = harness.RawServerMessages.Count;
        _ = await harness.Client.CallToolAsync(
            "symbol_info",
            new Dictionary<string, object?> { ["line"] = 1, ["column"] = 1 },
            cancellationToken: CancellationToken.None);
        var frames = harness.RawServerMessages
            .Skip(priorMessageCount)
            .Select(static raw => JsonNode.Parse(raw))
            .OfType<JsonObject>()
            .Where(static frame => frame["result"] is not null || frame["error"] is not null)
            .ToArray();
        Assert.HasCount(2, frames);
        Assert.AreEqual("input_required", frames[0]["result"]?["resultType"]?.GetValue<string>());
        var payload = ErrorPayload(frames[1]);
        Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>());
        Assert.AreEqual("ArgumentException", payload["exceptionType"]?.GetValue<string>());
        StringAssert.Contains(payload["message"]!.GetValue<string>(), "'filePath'");
        Assert.IsFalse(payload["message"]!.GetValue<string>().Contains("'workspaceId'", StringComparison.Ordinal));
        StringAssert.Contains(payload["schemaHint"]!.GetValue<string>(), "filePath");
    }

    [TestMethod]
    public async Task AutoResolvedWorkspaceId_IsNotReportedMissingWhenAnotherFieldIsOmitted()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        try
        {
            using var manager = CreateIsolatedWorkspaceManager();
            var workspace = await manager.LoadAsync(solutionPath, CancellationToken.None);
            try
            {
                foreach (var protocol in Protocols())
                {
                    await using var harness = await CreateHarnessAsync(protocol.Requested, manager);
                    var arguments = new Dictionary<string, object?>
                    {
                        ["filePath"] = Path.Combine(root, "SampleLib", "WidgetTarget.cs"),
                        ["line"] = 1,
                    };
                    var frame = await CallAndCaptureAsync(harness, "symbol_info", arguments);
                    var payload = ErrorPayload(frame);
                    Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>());
                    StringAssert.Contains(payload["message"]!.GetValue<string>(), "'column'");
                    Assert.IsFalse(payload["message"]!.GetValue<string>().Contains("'workspaceId'", StringComparison.Ordinal));
                    StringAssert.Contains(payload["schemaHint"]!.GetValue<string>(), "column");
                }
            }
            finally
            {
                manager.Close(workspace.WorkspaceId);
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task PublicArgumentRefusal_PreservesServerAuthoredMessageWithRequiredFieldAbsent()
    {
        foreach (var protocol in Protocols())
        {
            await using var harness = await CreateHarnessAsync(protocol.Requested);
            var frame = await CallAndCaptureAsync(harness, "synthetic_public_refusal", arguments: null);
            var payload = ErrorPayload(frame);
            Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>());
            Assert.AreEqual(
                "The server requires an explicit operator choice before this call.",
                payload["message"]?.GetValue<string>(),
                payload.ToJsonString());
            Assert.AreEqual("PublicArgumentException", payload["exceptionType"]?.GetValue<string>());

            var operationFrame = await CallAndCaptureAsync(harness, "synthetic_public_operation", arguments: null);
            var operationPayload = ErrorPayload(operationFrame);
            Assert.AreEqual("InvalidOperation", operationPayload["category"]?.GetValue<string>());
            Assert.AreEqual(
                "The server cannot continue this operation until its state changes.",
                operationPayload["message"]?.GetValue<string>());
            Assert.AreEqual("PublicInvalidOperationException", operationPayload["exceptionType"]?.GetValue<string>());
        }
    }

    private static WorkspaceManager CreateIsolatedWorkspaceManager()
    {
        var fileWatcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            fileWatcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocolVersion,
        IWorkspaceManager? workspaceManager = null,
        string? sanctionedRoot = null,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler = null)
    {
        var services = new ServiceCollection();
        if (workspaceManager is not null)
        {
            services.AddSingleton<IWorkspaceManager>(workspaceManager);
        }
        if (sanctionedRoot is not null)
        {
            services.AddSingleton(new SecurityOptions { SanctionedRoots = [sanctionedRoot] });
        }
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
            .WithMessageFilters(static filters =>
                filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(static filters =>
            {
                filters.AddCallToolFilter(StructuredCallToolFilter.Create);
                filters.AddCallToolFilter(next => (context, cancellationToken) =>
                    context.Params?.Name switch
                    {
                        "synthetic_public_refusal" => throw new PublicArgumentException(
                            "The server requires an explicit operator choice before this call.",
                            "path"),
                        "synthetic_public_operation" => throw new PublicInvalidOperationException(
                            "The server cannot continue this operation until its state changes."),
                        _ => next(context, cancellationToken),
                    });
            });
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"tool-call-error-{protocolVersion ?? "modern"}",
            clientCapabilities: elicitationHandler is null
                ? new ClientCapabilities()
                : new ClientCapabilities { Elicitation = new ElicitationCapability() },
            clientHandlers: elicitationHandler is null
                ? new McpClientHandlers()
                : new McpClientHandlers { ElicitationHandler = elicitationHandler },
            disposalFailureContext: "tool-call-error-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: protocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static async Task<JsonObject> CallAndCaptureAsync(
        InMemoryMcpClientServerHarness harness,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments)
    {
        var priorMessageCount = harness.RawServerMessages.Count;
        _ = await harness.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: CancellationToken.None);
        return FindSingleNewResponseFrame(harness.RawServerMessages, priorMessageCount);
    }

    private static void AssertFastFailFrame(JsonObject frame, string expectedMessage, bool modern)
    {
        var rawFrame = frame.ToJsonString();
        Assert.IsNull(frame["error"], rawFrame);
        var result = Assert.IsInstanceOfType<JsonObject>(frame["result"]);
        Assert.AreEqual(true, result["isError"]?.GetValue<bool>(), rawFrame);
        if (modern)
        {
            Assert.AreEqual("complete", result["resultType"]?.GetValue<string>(), rawFrame);
        }
        else
        {
            Assert.IsNull(result["resultType"], rawFrame);
        }

        var payload = ErrorPayload(frame);
        Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>(), rawFrame);
        StringAssert.Contains(payload["message"]?.GetValue<string>(), expectedMessage);
    }

    private static JsonObject ErrorPayload(JsonObject frame)
    {
        var result = Assert.IsInstanceOfType<JsonObject>(frame["result"]);
        var content = Assert.IsInstanceOfType<JsonArray>(result["content"]);
        var block = Assert.IsInstanceOfType<JsonObject>(content.Single());
        return Assert.IsInstanceOfType<JsonObject>(JsonNode.Parse(block["text"]!.GetValue<string>()));
    }

    private static IEnumerable<(string? Requested, bool Modern)> Protocols()
    {
        yield return ("2025-11-25", false);
        yield return (null, true);
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

        [McpServerTool(Name = "compile_check")]
        public static string CompileCheck(string? workspaceId = null) => workspaceId ?? "missing";

        [McpServerTool(Name = "symbol_info")]
        public static string SymbolInfo(
            string workspaceId,
            string filePath,
            int line,
            int column) => workspaceId;

        [McpServerTool(Name = "workspace_load")]
        public static string WorkspaceLoad(string path) => JsonSerializer.Serialize(new
        {
            workspaceId = "synthetic-recovered-workspace",
            loadedPath = path,
        });

        [McpServerTool(Name = "validate_workspace")]
        public static string ValidateWorkspace(string workspaceId) => workspaceId;

        [McpServerTool(Name = "synthetic_required_arguments")]
        public static string RequireTwo(string first, string second) => first + second;

        [McpServerTool(Name = "synthetic_public_refusal")]
        public static string PublicRefusal(string path) => path;

        [McpServerTool(Name = "synthetic_public_operation")]
        public static string PublicOperation(string path) => path;
    }
}
