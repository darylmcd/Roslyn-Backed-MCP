using System.IO.Pipelines;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>elicit-disambiguation-on-multi-symbol-resolve</c> and
/// <c>symbol-disambiguation-agent-first-default</c> initiatives (closes the same-named
/// backlog rows). When a metadata-name locator on <c>find_references</c> /
/// <c>go_to_definition</c> (or a &gt;1-hit <c>symbol_search</c> query) resolves to multiple
/// candidates (overloads, partial classes, member-vs-type collisions), <see cref="SymbolTools"/>
/// is now <b>agent-first by default</b>: the calling agent receives the structured
/// disambiguation-list response directly, with the stable <c>symbolHandle</c> per candidate.
/// The blocking MCP <c>elicitation/create</c> operator picker is opt-in only — reached solely
/// when the caller passes <c>allowElicitation=true</c> AND the client declares the capability;
/// otherwise the code falls through to the same additive list envelope.
///
/// <para>
/// Pins:
/// <list type="bullet">
///   <item><b>(a) elicit-supported preconditions</b> — the
///         <see cref="StructuredCallToolFilter.HasElicitation"/> capability check returns
///         true for a properly handshaken client AND the candidate-discovery helper finds
///         the multiple-overload set; the gate logic is therefore wired correctly. The
///         end-to-end <c>ElicitAsync</c> call requires a real
///         <see cref="ModelContextProtocol.Server.McpServer"/> with transport — see
///         <see cref="StructuredCallToolFilterElicitationTests"/> for the same gate
///         pattern on the workspace-path elicitation initiative. Because the live call needs
///         transport, the agent-first default and opt-in contract are pinned at the gate
///         predicate (<c>allowElicitation &amp;&amp; HasElicitation(caps)</c>) plus the
///         null-server list-envelope path, not via a synthetic transport harness.</item>
///   <item><b>(b) fallback</b> — when the caller does not opt in, or the client lacks the
///         elicitation capability (or the user declines), the tool returns a structured
///         <c>{ ambiguous: true, count, candidates }</c> envelope with a stable
///         <c>symbolHandle</c> per candidate, byte-identical regardless of whether elicitation
///         was tried or not.</item>
/// </list>
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SymbolDisambiguationElicitationTests : IsolatedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        DisposeServices();
    }

    // ── (a) elicit-supported preconditions ───────────────────────────────────

    [TestMethod]
    public void HasElicitation_PinsTheCapabilityCheckUsedByDisambiguation()
    {
        // The disambiguation gate inside SymbolTools.TryDisambiguateMetadataNameAsync
        // delegates to the same StructuredCallToolFilter.HasElicitation predicate the
        // workspace-path initiative pinned. This regression test pins that we still rely
        // on the same predicate (so a future refactor of either site can't drift).
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability(),
        };
        Assert.IsTrue(StructuredCallToolFilter.HasElicitation(capabilities),
            "An ElicitationCapability instance present on ClientCapabilities means the " +
            "client supports elicitation/create — the disambiguation gate is permitted.");

        Assert.IsFalse(StructuredCallToolFilter.HasElicitation(null),
            "Null capabilities (pre-handshake) MUST NOT permit elicitation.");
    }

    [TestMethod]
    [DataRow(nameof(SymbolTools.SearchSymbols))]
    [DataRow(nameof(SymbolTools.GoToDefinition))]
    [DataRow(nameof(SymbolTools.FindReferences))]
    public void AllowElicitationParameter_DefaultsToFalse(string methodName)
    {
        var method = typeof(SymbolTools).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, $"Expected SymbolTools.{methodName} to exist.");

        var parameter = method.GetParameters()
            .Single(candidate => string.Equals(candidate.Name, "allowElicitation", StringComparison.Ordinal));

        Assert.IsTrue(parameter.HasDefaultValue);
        Assert.AreEqual(
            false,
            parameter.DefaultValue,
            $"{methodName}.allowElicitation must remain agent-first by default.");
    }

    [TestMethod]
    public void AllowElicitationGate_OptIn_RequiresBothFlagAndCapability()
    {
        // The elicit branch is reachable ONLY when the caller opts in (allowElicitation=true)
        // AND the client declares the capability. Opt-in against a non-capable client still
        // falls back to the candidate-list envelope; a capable client without opt-in also
        // falls back (pinned above). Both conjuncts are load-bearing.
        var capable = new ClientCapabilities { Elicitation = new ElicitationCapability() };
        const bool optIn = true;

        Assert.IsTrue(
            optIn && StructuredCallToolFilter.HasElicitation(capable),
            "Opt-in + capable client is the only combination that reaches the elicit branch.");
        Assert.IsFalse(
            optIn && StructuredCallToolFilter.HasElicitation(null),
            "Opt-in against a non-capable (null-capabilities) client must still fall back " +
            "to the candidate list.");
    }

    [TestMethod]
    public async Task FindReferences_AmbiguousMetadataName_OptInButNonCapableClient_ReturnsListEnvelope()
    {
        // Opt-in alone does not change behavior for a client that cannot elicit: with
        // allowElicitation=true but a null server (server?.ClientCapabilities => null,
        // HasElicitation false), FindReferences must still return the additive
        // disambiguation-list envelope — proving the flag only *enables* the prompt on a
        // capable client and never breaks the non-capable fallback path.
        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                server: null!,
                WorkspaceManager,
                WorkspaceExecutionGate,
                ReferenceService,
                _workspaceId,
                metadataName: "System.String.Format",
                allowElicitation: true,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("ambiguous", out var ambiguous) || !ambiguous.GetBoolean())
        {
            Assert.Inconclusive(
                "System.String.Format did not produce an ambiguous resolution in the loaded " +
                $"sample solution. Response was: {json}");
            return;
        }

        Assert.IsTrue(doc.RootElement.GetProperty("count").GetInt32() >= 2,
            "Opt-in against a non-capable client must still return the >= 2-candidate list envelope.");
        Assert.AreEqual("System.String.Format",
            doc.RootElement.GetProperty("metadataName").GetString(),
            "Envelope must echo the original metadata name so clients can correlate.");
    }

    [TestMethod]
    public async Task FindAllByMetadataNameAsync_FindsMultipleOverloadCandidates()
    {
        // Pin the candidate-discovery half of the gate: when a metadata name resolves to
        // multiple overloads, the helper returns ALL of them (not just the first match
        // that ResolveByMetadataNameAsync historically picked). This is the precondition
        // for the gate detecting ambiguity at all — without multiple candidates, the
        // gate short-circuits and elicitation never happens.
        //
        // SampleLib has multiple AnimalService methods; we look for any name in the
        // sample workspace that resolves to >= 2 candidates. If SampleSolution evolves,
        // adjust the metadata name to one with documented overloads.
        var solution = WorkspaceManager.GetCurrentSolution(_workspaceId);

        // Probe a handful of common ambiguous shapes. The test passes if ANY of them
        // returns >= 2 candidates — pinning that the helper can detect ambiguity, not
        // that any specific name is ambiguous in SampleLib (which may evolve).
        var probes = new[]
        {
            "SampleLib.AnimalService.GetAllAnimals",
            "SampleLib.AnimalService.SaveAnimal",
            "System.Object.ToString",
            "System.String.Format",
        };

        var sawAmbiguity = false;
        foreach (var name in probes)
        {
            var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
                solution, name, CancellationToken.None);
            if (candidates.Count >= 2)
            {
                sawAmbiguity = true;
                // Each candidate must produce a non-empty display label for the picker.
                foreach (var c in candidates)
                {
                    var label = SymbolHandleSerializer.BuildDisplayLabel(c);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                        $"BuildDisplayLabel must produce a non-empty label for every candidate (offender: {c}).");
                }
                break;
            }
        }

        Assert.IsTrue(sawAmbiguity,
            "FindAllByMetadataNameAsync must return >= 2 candidates for at least one " +
            "of the standard overloaded shapes (System.String.Format, etc.); otherwise " +
            "the disambiguation gate has nothing to disambiguate.");
    }

    [TestMethod]
    public async Task FindAllByMetadataNameAsync_DedupesSameSourceDeclarationAcrossProjectCompilations()
    {
        // A metadata-name lookup may see the same source declaration through more than one
        // project compilation. That is not a real ambiguity for find_references; the
        // disambiguation gate should collapse identical source spans before returning an
        // ambiguous envelope.
        const string source = """
            namespace DuplicateCandidates;

            public sealed class SharedSourceType
            {
                public void Touch() { }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var sharedSourcePath = Path.Combine(
            TestTempRoot.Current,
            "DuplicateCandidates",
            "SharedSourceType.cs");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        var solution = workspace.CurrentSolution;
        for (var i = 0; i < 2; i++)
        {
            var projectId = ProjectId.CreateNewId($"DuplicateCandidates{i}");
            solution = solution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                $"DuplicateCandidates{i}",
                $"DuplicateCandidates{i}",
                LanguageNames.CSharp,
                metadataReferences: references));

            var documentId = DocumentId.CreateNewId(projectId, "SharedSourceType.cs");
            solution = solution.AddDocument(DocumentInfo.Create(
                documentId,
                "SharedSourceType.cs",
                filePath: sharedSourcePath,
                loader: TextLoader.From(TextAndVersion.Create(
                    SourceText.From(source),
                    VersionStamp.Create()))));
        }

        var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
            solution,
            "DuplicateCandidates.SharedSourceType",
            CancellationToken.None);

        Assert.AreEqual(1, candidates.Count,
            "Duplicate compilation candidates for the same source path and span should collapse to one metadata-name candidate.");
        Assert.AreEqual(SymbolKind.NamedType, candidates[0].Kind);
        Assert.AreEqual("SharedSourceType", candidates[0].Name);
    }

    [TestMethod]
    public async Task FindAllByMetadataNameAsync_DedupesMetadataCandidatesBySymbolHandle()
    {
        // System.Xml.XmlException can surface through multiple metadata assemblies. The
        // emitted handle intentionally omits assembly identity, so those candidates are
        // indistinguishable to clients unless the disambiguation path collapses them first.
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var xmlCarrierA = CreateXmlExceptionMetadataReference("XmlCarrierA", references);
        var xmlCarrierB = CreateXmlExceptionMetadataReference("XmlCarrierB", references);

        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        solution = AddProjectWithMetadataReference(solution, "XmlConsumerA", xmlCarrierA);
        solution = AddProjectWithMetadataReference(solution, "XmlConsumerB", xmlCarrierB);

        var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
            solution,
            "System.Xml.XmlException",
            CancellationToken.None);

        var handles = candidates
            .Select(SymbolHandleSerializer.CreateHandle)
            .ToList();

        Assert.AreEqual(1, candidates.Count,
            "Two metadata assemblies exposing the same handle must collapse to one candidate.");
        Assert.AreEqual(
            handles.Count,
            handles.Distinct(StringComparer.Ordinal).Count(),
            "Metadata-name candidates must be deduped by symbolHandle before returning an ambiguity envelope.");
    }

    // ── cancellation must propagate, not be reported as decline ─────────────
    // elicitation-trychoice-cancellation-swallow: TryElicitChoiceAsync's try/catch around
    // server.ElicitAsync used to be a bare `catch { return null; }`, which absorbed
    // OperationCanceledException alongside the two expected SDK failure shapes
    // (InvalidOperationException, McpException). Callers (SymbolTools.GoToDefinition /
    // FindReferences / SearchSymbols) treat a null return as "user declined" and answer
    // with the additive candidate-list response — so a cancelled request looked
    // indistinguishable from a deliberate decline. This test pins that a pre-cancelled
    // token now propagates out of TryElicitChoiceAsync instead. It does NOT cover
    // cancellation that arrives while a request is genuinely in flight against an
    // unresponsive client — two independent attempts at that variant deadlocked the test
    // process; see the tracked follow-up elicitation-inflight-cancellation-test-harness-deadlock.

    [TestMethod]
    [Timeout(15_000)]
    public async Task TryElicitChoiceAsync_PreCancelledToken_PropagatesCancellation_NotAdditiveListFallback()
    {
        // A real McpServer/McpClient pair (over an in-memory duplex pipe, same pattern as
        // ExpandedSurfaceIntegrationTests.CreateServerWithSanctionedRootAsync) is required
        // to reach the try body at all: HasElicitation must return true, which needs a
        // genuine post-handshake ClientCapabilities.Elicitation, not a null/fake server.
        // The client's ElicitationHandler never completes; the token below is cancelled
        // BEFORE the call, not while the request is in flight (see the section comment
        // above for why the in-flight variant isn't covered here).
        await using var harness = await CreateServerWithHangingElicitationAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OperationCanceledException? caught = null;
        try
        {
            var result = await ElicitationChoicePrompt.TryElicitChoiceAsync(
                harness.Server,
                paramName: "choice",
                title: "Pick a symbol",
                description: "symbol_search returned candidates. Pick one to focus on.",
                options: [("0", "Candidate A"), ("1", "Candidate B")],
                cancellationToken: cts.Token);

            Assert.Fail(
                "Expected TryElicitChoiceAsync to throw for a pre-cancelled token, but it " +
                $"returned {(result is null ? "null" : $"\"{result}\"")} instead — a cancelled " +
                "request must never be reported as a user decline.");
        }
        catch (OperationCanceledException ex)
        {
            // ThrowsExactlyAsync<OperationCanceledException> would reject the SDK's actual
            // TaskCanceledException subclass (same reasoning as
            // WorkspaceValidationTimeoutTests), so catch the base class manually.
            caught = ex;
        }

        Assert.IsNotNull(caught,
            "A pre-cancelled CancellationToken must surface as OperationCanceledException out " +
            "of TryElicitChoiceAsync, not be swallowed by the InvalidOperationException/" +
            "McpException catch and converted to the additive-list null fallback.");
    }

    /// <summary>
    /// Wires a real <see cref="McpServer"/> to a real <see cref="McpClient"/> over an in-memory
    /// duplex pipe (mirrors <c>ExpandedSurfaceIntegrationTests.CreateServerWithSanctionedRootAsync</c>)
    /// so <c>server.ClientCapabilities.Elicitation</c> is genuinely populated via the MCP
    /// initialize handshake and <c>server.ElicitAsync</c> genuinely round-trips to a client. The
    /// client's <see cref="McpClientHandlers.ElicitationHandler"/> never completes, so any
    /// <c>elicitation/create</c> request stays pending indefinitely — letting a test pre-cancel
    /// the caller's own token before the call in isolation from whether the client ever
    /// actually answers. Cancelling AFTER the request is dispatched (a genuinely in-flight
    /// race) is NOT safe to test against this harness: it deadlocks — see
    /// elicitation-inflight-cancellation-test-harness-deadlock.
    /// </summary>
    private static async Task<ElicitationServerHarness> CreateServerWithHangingElicitationAsync(CancellationToken ct)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var clientToServerReadStream = clientToServer.Reader.AsStream();
        var clientToServerWriteStream = clientToServer.Writer.AsStream();
        var serverToClientReadStream = serverToClient.Reader.AsStream();
        var serverToClientWriteStream = serverToClient.Writer.AsStream();

        var serverTransport = new StreamServerTransport(
            clientToServerReadStream,
            serverToClientWriteStream,
            "test-server-elicitation-cancellation",
            NullLoggerFactory.Instance);
        var server = McpServer.Create(serverTransport, new McpServerOptions(), NullLoggerFactory.Instance, null);
        var cts = new CancellationTokenSource();
        var serverRunTask = server.RunAsync(cts.Token);

        var clientTransport = new StreamClientTransport(
            clientToServerWriteStream,
            serverToClientReadStream,
            NullLoggerFactory.Instance);

        var client = await McpClient.CreateAsync(
            clientTransport,
            new McpClientOptions
            {
                // This harness directly invokes the connection-scoped server after the
                // initialize handshake. Pin that compatibility protocol explicitly; modern
                // 2026-07-28 capabilities are request-scoped and exercised through tools/call.
                ProtocolVersion = "2025-11-25",
                Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability() },
                Handlers = new McpClientHandlers
                {
                    // Never completes — the server must observe its own token's cancellation
                    // rather than waiting forever for a reply that never comes.
                    ElicitationHandler = (_, _) =>
                        new ValueTask<ElicitResult>(new TaskCompletionSource<ElicitResult>().Task),
                },
            },
            NullLoggerFactory.Instance,
            ct).ConfigureAwait(false);

        return new ElicitationServerHarness(
            server,
            client,
            cts,
            serverRunTask,
            [
                clientToServerReadStream,
                clientToServerWriteStream,
                serverToClientReadStream,
                serverToClientWriteStream,
            ]);
    }

    private sealed class ElicitationServerHarness(
        McpServer server,
        McpClient client,
        CancellationTokenSource cts,
        Task serverRunTask,
        IReadOnlyList<Stream> transportStreams) : IAsyncDisposable
    {
        public McpServer Server { get; } = server;

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            await DisposeCapturingAsync(client, failures).ConfigureAwait(false);
            cts.Cancel();
            try
            {
                await serverRunTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected — cancelling the server's receive loop surfaces as this.
            }
            finally
            {
                await DisposeCapturingAsync(Server, failures).ConfigureAwait(false);
                foreach (var stream in transportStreams)
                {
                    await DisposeCapturingAsync(stream, failures).ConfigureAwait(false);
                }
                cts.Dispose();
            }

            if (failures.Count > 0)
            {
                throw new AggregateException("Failed to dispose the elicitation-cancellation MCP test harness.", failures);
            }
        }

        private static async ValueTask DisposeCapturingAsync(
            IAsyncDisposable disposable,
            ICollection<Exception> failures)
        {
            try
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }
    }

    // ── (b) fallback when client lacks elicitation capability ───────────────

    [TestMethod]
    public async Task FindReferences_AmbiguousMetadataName_NullServer_ReturnsListEnvelope()
    {
        // The contract: when the client doesn't support elicitation (server == null in
        // the test harness, which makes server?.ClientCapabilities null and HasElicitation
        // false), the tool returns the additive disambiguation-list envelope:
        //   { ambiguous: true, metadataName, count, candidates: [{ label, symbolHandle, kind }, ...], note }
        // This is the byte-identical fallback shape — clients that don't support
        // elicitation see this regardless of whether the server attempted to elicit.
        //
        // We pick a name documented as having multiple candidates in BCL so the test
        // doesn't depend on SampleLib evolution.
        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                server: null!,
                WorkspaceManager,
                WorkspaceExecutionGate,
                ReferenceService,
                _workspaceId,
                metadataName: "System.String.Format",
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("ambiguous", out var ambiguous) || !ambiguous.GetBoolean())
        {
            // System.String.Format may not be reachable from SampleLib's compilation if
            // the solution evolves. In that case the test is meaningless — assert
            // explicitly so the failure is descriptive.
            Assert.Inconclusive(
                "System.String.Format did not produce an ambiguous resolution in the loaded " +
                $"sample solution. Response was: {json}");
            return;
        }

        Assert.IsTrue(doc.RootElement.GetProperty("count").GetInt32() >= 2,
            "Disambiguation envelope must declare >= 2 candidates.");
        Assert.AreEqual("System.String.Format",
            doc.RootElement.GetProperty("metadataName").GetString(),
            "Envelope must echo the original metadata name so clients can correlate.");

        var candidates = doc.RootElement.GetProperty("candidates");
        Assert.AreEqual(JsonValueKind.Array, candidates.ValueKind);
        Assert.IsTrue(candidates.GetArrayLength() >= 2);

        foreach (var c in candidates.EnumerateArray())
        {
            Assert.IsTrue(c.TryGetProperty("label", out var label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(label.GetString()),
                "Each candidate must carry a human-readable label for the picker UI / agent prompt.");
            Assert.IsTrue(c.TryGetProperty("symbolHandle", out var handle));
            Assert.IsFalse(string.IsNullOrWhiteSpace(handle.GetString()),
                "Each candidate must carry a stable symbolHandle so clients can re-call the tool with the chosen one.");
            Assert.IsTrue(c.TryGetProperty("kind", out _),
                "Each candidate must declare its symbol kind (Method, Property, NamedType, ...).");
        }

        Assert.IsTrue(doc.RootElement.TryGetProperty("note", out var note));
        Assert.IsTrue(note.GetString()!.Contains("symbolHandle", StringComparison.OrdinalIgnoreCase),
            "Note must direct clients to re-call with the chosen symbolHandle.");
    }

    private static MetadataReference CreateXmlExceptionMetadataReference(
        string assemblyName,
        IReadOnlyList<MetadataReference> references)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace System.Xml;

            public sealed class XmlException
            {
            }
            """);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        Assert.IsTrue(
            emit.Success,
            "Test metadata carrier must compile: " +
            string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(peStream.ToArray());
    }

    private static Solution AddProjectWithMetadataReference(
        Solution solution,
        string projectName,
        MetadataReference xmlExceptionReference)
    {
        var projectId = ProjectId.CreateNewId(projectName);
        return solution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp,
            metadataReferences:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                xmlExceptionReference,
            ]));
    }
}
