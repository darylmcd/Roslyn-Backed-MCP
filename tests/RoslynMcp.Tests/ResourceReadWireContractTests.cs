using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// resource-read-protocol-error-semantics (parts 1 + 2): table-driven raw JSON-RPC
/// <c>resources/read</c> failure matrix across BOTH resource families (workspace + server
/// catalog) and BOTH protocol eras (2025-11-25 and 2026-07-28). Pins, at the wire level:
/// <list type="bullet">
///   <item>NotFound → <c>-32002</c> (ResourceNotFound) under 2025-11-25 and <c>-32602</c>
///         (InvalidParams) under 2026-07-28, including the <c>source_file</c>
///         <c>McpToolException</c> wrapper whose INNER <c>KeyNotFoundException</c> must drive
///         classification (a regression to the InternalError fallback surfaces as
///         <c>-32603</c> and fails these rows).</item>
///   <item>Caller-input faults (malformed <c>lineRange</c>, non-absolute <c>filePath</c>,
///         malformed pagination slots, unsupported catalog-diff versions) → <c>-32602</c> in
///         both eras.</item>
///   <item>An unexpected handler failure → sanitized <c>-32603</c> carrying a correlation id
///         but no exception type name, stack frame, or absolute server-side path. The
///         forbidden-path probe compares against the JSON-ESCAPED form of the path — a raw
///         single-backslash <c>Contains</c> can never match a serialized frame and would be a
///         dead assertion (see the falsifiability self-check).</item>
///   <item>Failed reads answer on the JSON-RPC error channel: the response frame carries an
///         <c>error</c> member and NO <c>result.contents</c> body.</item>
///   <item>One success row pins that <c>TimeToLive</c>/<c>CacheScope</c> normalization still
///         applies to migrated endpoints (hints present under 2026-07-28, absent under
///         2025-11-25).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ResourceReadWireContractTests
{
    private const string WorkspaceId = "ws-wire";

    /// <summary>
    /// A server-side path SHAPE that must never reach the wire. Deliberately Windows-shaped on
    /// every OS: it is a literal embedded in the scripted manager's exception message (never
    /// resolved against the filesystem), so keeping it constant keeps the leak probe's escaped
    /// form — and its falsifiability self-check — identical on both CI legs.
    /// </summary>
    private const string SecretServerPath = @"C:\secret-server\Internals.cs";

    /// <summary>
    /// Fixture root for the workspace-family rows, derived per-OS. The handlers precondition on
    /// <see cref="Path.IsPathFullyQualified"/>, and Unix rooting requires a leading <c>/</c> —
    /// a hardcoded <c>C:\ws\</c> is NOT fully qualified on Linux, so every workspace row would
    /// short-circuit to InvalidArgument/-32602 before reaching the scripted manager and the
    /// era-correct codes (-32002 / -32603) would never be exercised on the ubuntu leg.
    /// </summary>
    private static readonly string _fixtureRoot = OperatingSystem.IsWindows() ? @"C:\ws\" : "/ws/";

    /// <summary>
    /// URL-encoded <c>{filePath}</c> segment for a fixture file. Encoding at build time keeps
    /// the URI legal on both legs (Windows <c>:</c>/<c>\</c> and Unix <c>/</c> are all reserved
    /// in the segment grammar); the handler's normalizer decodes it back.
    /// </summary>
    private static string FileSegment(string fileName) => Uri.EscapeDataString(_fixtureRoot + fileName);

    /// <summary>
    /// The two negotiated protocol eras every wire assertion runs under. Shared by the failure
    /// matrix and the success row so a new era is added in exactly one place.
    /// </summary>
    private static readonly (string? RequestedVersion, string ExpectedVersion, bool SupportsJuly2026Features)[] _protocolEras =
    [
        ("2025-11-25", "2025-11-25", false),
        (null, "2026-07-28", true),
    ];

    [TestMethod]
    public void ErrorCodeMapping_CoversEveryDeclaredCategory_AndFailsClosedForUnknownValues()
    {
        foreach (var category in Enum.GetValues<ToolErrorHandler.ToolErrorCategory>())
        {
            Assert.IsTrue(
                ResourceReadResultFilter.HasExplicitErrorCodeMapping(category),
                $"Tool error category '{category}' needs an explicit resources/read wire-code decision.");
        }

        Assert.AreEqual(
            McpErrorCode.InternalError,
            ResourceReadResultFilter.MapErrorCode((ToolErrorHandler.ToolErrorCategory)int.MaxValue, false));
    }

    private sealed record FailureCase(
        string Label,
        string Uri,
        int LegacyCode,
        int July2026Code,
        string MessageMustContain,
        string[] ForbiddenLiterals);

    private static readonly FailureCase[] _failureCases =
    [
        // Workspace family — source_file wraps its causes in McpToolException; the filter
        // must classify the INNER KeyNotFoundException (NotFound), not the wrapper
        // (InternalError fallback → -32603, which fails this row in both eras).
        new("workspace/source_file NotFound (McpToolException inner KeyNotFoundException)",
            "roslyn://workspace/" + WorkspaceId + "/file/" + FileSegment("Missing.cs"),
            LegacyCode: -32002,
            July2026Code: -32602,
            MessageMustContain: "NotFound",
            ForbiddenLiterals: ["KeyNotFoundException", "McpToolException"]),
        new("workspace/source_file_lines malformed lineRange",
            "roslyn://workspace/" + WorkspaceId + "/file/" + FileSegment("Present.cs") + "/lines/abc-def",
            LegacyCode: -32602,
            July2026Code: -32602,
            // "param: lineRange" proves the HANDLER's validation fired — an SDK binding
            // failure also produces InvalidArgument/-32602 but names "param: arguments".
            MessageMustContain: "param: lineRange",
            ForbiddenLiterals: ["ArgumentException"]),
        new("workspace/source_file non-absolute filePath is caller fault (-32602, never -32603)",
            "roslyn://workspace/" + WorkspaceId + "/file/not-absolute.cs",
            LegacyCode: -32602,
            July2026Code: -32602,
            MessageMustContain: "param: filePath",
            ForbiddenLiterals: ["ArgumentException", "InvalidOperation"]),
        new("workspace/source_file unexpected handler failure is sanitized",
            "roslyn://workspace/" + WorkspaceId + "/file/" + FileSegment("Boom.cs"),
            LegacyCode: -32603,
            July2026Code: -32603,
            MessageMustContain: "correlationId",
            ForbiddenLiterals: ["NotImplementedException", "Internals.cs", "   at "]),
        // Server-catalog family — the three call sites migrated by part 2.
        new("catalog/tools page malformed offset slot",
            "roslyn://server/catalog/tools/abc/50",
            LegacyCode: -32602,
            July2026Code: -32602,
            MessageMustContain: "param: offset",
            ForbiddenLiterals: ["ArgumentException"]),
        new("catalog/prompts page malformed limit slot",
            "roslyn://server/catalog/prompts/0/xyz",
            LegacyCode: -32602,
            July2026Code: -32602,
            MessageMustContain: "param: limit",
            ForbiddenLiterals: ["ArgumentException"]),
        new("catalog-diff unsupported version pair",
            "roslyn://server/catalog-diff/v2.3.0/current",
            LegacyCode: -32602,
            July2026Code: -32602,
            MessageMustContain: "Unsupported catalog diff",
            // The raw ArgumentException message echoes the pair as 'v2.3.0' -> 'current';
            // the sanitized remediation must not. (The bare version string still appears in
            // the echoed *request URI*, which is caller-supplied and allowed.)
            ForbiddenLiterals: ["ArgumentException", "'v2.3.0' ->"]),
    ];

    [TestMethod]
    public async Task ResourceRead_FailureMatrix_AnswersOnErrorChannelWithEraCorrectCodes()
    {
        foreach (var protocol in _protocolEras)
        {
            await using var harness = await CreateHarnessAsync(protocol.RequestedVersion, protocol.ExpectedVersion);
            Assert.AreEqual(protocol.ExpectedVersion, harness.Client.NegotiatedProtocolVersion);

            foreach (var failureCase in _failureCases)
            {
                var label = $"[{protocol.ExpectedVersion}] {failureCase.Label}";
                var before = harness.RawServerMessages.Count;

                McpException? clientError = null;
                try
                {
                    _ = await harness.Client.ReadResourceAsync(
                        new ReadResourceRequestParams { Uri = failureCase.Uri },
                        CancellationToken.None);
                }
                catch (McpException ex)
                {
                    clientError = ex;
                }

                Assert.IsNotNull(clientError, $"{label}: expected a JSON-RPC protocol error, got a successful result.");

                var rawFrame = FindSingleNewResponseFrame(harness.RawServerMessages, before, label);
                using var frame = JsonDocument.Parse(rawFrame);
                var root = frame.RootElement;

                // Failure answers on the error channel: an `error` member and NO `result`
                // (so no `result.contents` body carrying a serialized error document).
                Assert.IsTrue(root.TryGetProperty("error", out var error),
                    $"{label}: response frame carries no `error` member: {rawFrame}");
                Assert.IsFalse(root.TryGetProperty("result", out _),
                    $"{label}: failed read must not carry `result` (contents body): {rawFrame}");

                var expectedCode = protocol.SupportsJuly2026Features ? failureCase.July2026Code : failureCase.LegacyCode;
                Assert.AreEqual(expectedCode, error.GetProperty("code").GetInt32(),
                    $"{label}: wrong JSON-RPC error code. Frame: {rawFrame}");

                var message = error.GetProperty("message").GetString() ?? string.Empty;
                StringAssert.Contains(message, failureCase.MessageMustContain, $"{label}: message '{message}'");
                AssertCarriesRealCorrelationId(message, label);

                foreach (var forbidden in failureCase.ForbiddenLiterals)
                {
                    Assert.IsFalse(rawFrame.Contains(forbidden, StringComparison.Ordinal),
                        $"{label}: frame leaks forbidden literal '{forbidden}': {rawFrame}");
                }
            }
        }
    }

    [TestMethod]
    public async Task ResourceRead_UnexpectedFailure_NeverLeaksServerSidePath_EscapedProbeIsFalsifiable()
    {
        // A serialized JSON frame escapes backslashes, so a raw single-backslash Contains can
        // NEVER match even when the path genuinely leaks. Build the probe in the frame's own
        // encoding and prove it is falsifiable before trusting the negative assertion.
        var escapedPathProbe = JsonSerializer.Serialize(SecretServerPath).Trim('"');
        Assert.AreNotEqual(SecretServerPath, escapedPathProbe,
            "Probe self-check: the JSON-escaped form must differ from the raw path (backslashes escape).");
        StringAssert.Contains(
            JsonSerializer.Serialize(new { message = $"boom at {SecretServerPath}" }),
            escapedPathProbe,
            "Probe self-check: a deliberately leaked frame MUST match the escaped probe — " +
            "otherwise the negative assertion below is dead.");

        await using var harness = await CreateHarnessAsync(requestedVersion: null, expectedVersion: "2026-07-28");
        var before = harness.RawServerMessages.Count;

        await Assert.ThrowsAsync<McpException>(async () =>
            _ = await harness.Client.ReadResourceAsync(
                new ReadResourceRequestParams { Uri = "roslyn://workspace/" + WorkspaceId + "/file/" + FileSegment("Boom.cs") },
                CancellationToken.None));

        var rawFrame = FindSingleNewResponseFrame(harness.RawServerMessages, before, "sanitized -32603");
        using var frame = JsonDocument.Parse(rawFrame);
        Assert.AreEqual(-32603, frame.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.IsFalse(rawFrame.Contains(escapedPathProbe, StringComparison.Ordinal),
            $"-32603 frame leaks the server-side path (escaped probe matched): {rawFrame}");
        Assert.IsFalse(rawFrame.Contains("NotImplementedException", StringComparison.Ordinal),
            $"-32603 frame leaks the exception type name: {rawFrame}");
    }

    [TestMethod]
    public async Task ResourceRead_MigratedEndpointSuccess_KeepsCacheHintNormalizationPerEra()
    {
        foreach (var protocol in _protocolEras)
        {
            await using var harness = await CreateHarnessAsync(protocol.RequestedVersion, protocol.ExpectedVersion);
            var before = harness.RawServerMessages.Count;

            var result = await harness.Client.ReadResourceAsync(
                new ReadResourceRequestParams { Uri = "roslyn://server/catalog/tools/0/5" },
                CancellationToken.None);
            Assert.AreEqual(1, result.Contents.Count);

            var rawFrame = FindSingleNewResponseFrame(
                harness.RawServerMessages, before, $"[{protocol.ExpectedVersion}] tools page success");
            using var frame = JsonDocument.Parse(rawFrame);
            var resultElement = frame.RootElement.GetProperty("result");
            Assert.IsTrue(resultElement.TryGetProperty("contents", out _),
                $"success frame must carry result.contents: {rawFrame}");

            if (protocol.SupportsJuly2026Features)
            {
                Assert.AreEqual(0L, resultElement.GetProperty("ttlMs").GetInt64(),
                    "2026-07-28 read must stamp ttlMs=0 (immediately stale).");
                Assert.AreEqual("private", resultElement.GetProperty("cacheScope").GetString());
            }
            else
            {
                Assert.IsFalse(resultElement.TryGetProperty("ttlMs", out _),
                    "2025-11-25 read must not carry ttlMs.");
                Assert.IsFalse(resultElement.TryGetProperty("cacheScope", out _),
                    "2025-11-25 read must not carry cacheScope.");
            }
        }
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? requestedVersion, string expectedVersion)
    {
        var selection = ToolTierSelection.All;
        var gate = new PassThroughWorkspaceExecutionGate();
        var manager = new ScriptedWorkspaceManager();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        var hostAssembly = typeof(RoslynMcp.Host.Stdio.HostAssemblyMarker).Assembly;
        // The fakes must be registered in the HOST builder (used at resource-registration
        // time, where the SDK decides which handler parameters are DI-injected) AND in the
        // per-request server services below — with only the request-side registration the
        // binder treats `gate`/`workspace` as missing caller arguments and every read fails
        // as InvalidParams before reaching the handler.
        builder.Services.AddSingleton<IWorkspaceExecutionGate>(gate);
        builder.Services.AddSingleton<IWorkspaceManager>(manager);
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "roslyn-mcp-test", Version = "1.0.0" };
            })
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly)
            // Production parity (Program.cs): the incoming-message filter is what opens the
            // per-message RequestCorrelationContext scope. WITHOUT it every sanitized error
            // frame renders the literal "unavailable", and any assertion that merely looks for
            // the word "correlationId" passes on the hardcoded label alone — a dead assertion.
            .WithMessageFilters(filters => filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(filters => filters.AddReadResourceFilter(ResourceReadResultFilter.Create));
        builder.Services.AddRoslynMcpSurfaceRegistrationPolicy(selection);
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"resource-read-wire-{expectedVersion}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: $"resource-read-wire-{expectedVersion}",
            cancellationToken: CancellationToken.None,
            protocolVersion: requestedVersion,
            serverOptions: options,
            serverServicesFactory: () => new ServiceCollection()
                .AddSingleton(selection)
                .AddSingleton<IWorkspaceExecutionGate>(gate)
                .AddSingleton<IWorkspaceManager>(manager)
                .BuildServiceProvider(),
            captureServerMessages: true);
    }

    /// <summary>
    /// Asserts the sanitized error message carries a REAL correlation id — a 32-char lowercase
    /// hex <c>Guid("N")</c> minted by <c>RequestCorrelationContext.Begin()</c> — and explicitly
    /// NOT the <c>"unavailable"</c> no-context sentinel. Asserting only that the word
    /// "correlationId" appears is satisfied by the hardcoded label in the message template and
    /// can never fail.
    /// </summary>
    private static void AssertCarriesRealCorrelationId(string message, string label)
    {
        const string marker = "correlationId: ";
        var start = message.LastIndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"{label}: message carries no correlationId field: '{message}'");

        var correlationId = message[(start + marker.Length)..].TrimEnd(')');
        Assert.AreNotEqual("unavailable", correlationId,
            $"{label}: correlationId is the no-context sentinel — the per-message correlation " +
            $"scope never opened, so no real id reached the wire. Message: '{message}'");
        Assert.AreEqual(32, correlationId.Length,
            $"{label}: correlationId '{correlationId}' is not a 32-char Guid(\"N\"). Message: '{message}'");
        Assert.IsTrue(
            correlationId.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            $"{label}: correlationId '{correlationId}' is not lowercase hex. Message: '{message}'");
    }

    /// <summary>
    /// Finds the single JSON-RPC response frame (a top-level object with an <c>id</c> plus a
    /// <c>result</c> or <c>error</c> member) written by the server after <paramref name="skip"/>.
    /// </summary>
    private static string FindSingleNewResponseFrame(IReadOnlyList<string> messages, int skip, string label)
    {
        var responses = new List<string>();
        for (var i = skip; i < messages.Count; i++)
        {
            using var document = JsonDocument.Parse(messages[i]);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("id", out _) &&
                (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)))
            {
                responses.Add(messages[i]);
            }
        }

        Assert.AreEqual(1, responses.Count, $"{label}: expected exactly one new response frame.");
        return responses[0];
    }

    /// <summary>
    /// Scripted <see cref="IWorkspaceManager"/> for the wire matrix: <c>GetSourceTextAsync</c>
    /// resolves by file name — <c>Present.cs</c> returns text, <c>Missing.cs</c> returns
    /// <see langword="null"/> (handler raises <see cref="KeyNotFoundException"/>), and
    /// <c>Boom.cs</c> throws an unexpected exception whose message deliberately embeds an
    /// absolute server-side path that must never reach the wire.
    /// </summary>
    private sealed class ScriptedWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct)
        {
            if (filePath.EndsWith("Boom.cs", StringComparison.Ordinal))
            {
                throw new NotImplementedException($"handler blew up at {SecretServerPath} line 42");
            }

            return Task.FromResult(filePath.EndsWith("Present.cs", StringComparison.Ordinal)
                ? "// present\nclass C { }\n"
                : (string?)null);
        }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => workspaceId == WorkspaceId;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
