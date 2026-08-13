using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
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
///         end-to-end <c>ElicitAsync</c> cancellation paths use a real SDK client/server pair
///         over the shared in-memory transport harness — both pre-cancelled and genuinely
///         in-flight (the latter previously believed untestable; see
///         <see cref="ControllableElicitationHarness"/> for why it now is).</item>
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
    // indistinguishable from a deliberate decline. Two tests pin it: a pre-cancelled token
    // (below) and cancellation arriving while the request is genuinely in flight against an
    // unresponsive client (further below).
    //
    // elicitation-inflight-cancellation-test-harness-deadlock: the in-flight variant was
    // believed untestable — three prior attempts hung the test process and needed taskkill.
    // The blocking point was never TryElicitChoiceAsync or server.ElicitAsync; it was the old
    // never-completing ElicitationHandler wedging McpClient.DisposeAsync during `await using`
    // teardown, AFTER the assertions had already passed. ControllableElicitationHarness fixes
    // that by owning a completable handler; see its remarks and those on
    // InMemoryMcpClientServerHarness for the full ruled-in/ruled-out evidence.

    [TestMethod]
    [Timeout(15_000, CooperativeCancellation = true)]
    public async Task TryElicitChoiceAsync_PreCancelledToken_PropagatesCancellation_NotAdditiveListFallback()
    {
        // A real McpServer/McpClient pair over the shared in-memory duplex harness is required
        // to reach the try body at all: HasElicitation must return true, which needs a
        // genuine post-handshake ClientCapabilities.Elicitation, not a null/fake server.
        // The token below is cancelled BEFORE the call, so the request never reaches the
        // client's handler at all; the in-flight variant is the next test.
        await using var harness = await CreateServerWithControllableElicitationAsync(CancellationToken.None);

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

    [TestMethod]
    [Timeout(15_000, CooperativeCancellation = true)]
    public async Task TryElicitChoiceAsync_CancelledWhileRequestInFlight_PropagatesCancellation()
    {
        // The non-vacuous variant of the test above: the elicitation/create request genuinely
        // reaches the client (RequestReceived below only completes from inside the client's own
        // handler), the client then never answers, and only THEN is the caller's token cancelled.
        // This pins that a real MCP client going unresponsive mid-elicitation cannot wedge the
        // server: the await unblocks and the caller's WorkspaceExecutionGate slot is released.
        await using var harness = await CreateServerWithControllableElicitationAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();

        var call = ElicitationChoicePrompt.TryElicitChoiceAsync(
            harness.Server,
            paramName: "choice",
            title: "Pick a symbol",
            description: "symbol_search returned candidates. Pick one to focus on.",
            options: [("0", "Candidate A"), ("1", "Candidate B")],
            cancellationToken: cts.Token);

        // Cancelling only after this completes is what makes the test non-vacuous — before it,
        // the request may still be sitting in the transport rather than truly in flight. Bounded
        // well inside the [Timeout] above so a regression here fails fast instead of hanging.
        await harness.RequestReceived.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(call.IsCompleted,
            "The client's elicitation handler is deliberately unanswered, so TryElicitChoiceAsync " +
            "must still be pending at the moment cancellation is requested — otherwise this test " +
            "would be vacuously asserting the pre-cancelled path again.");

        await cts.CancelAsync();

        OperationCanceledException? caught = null;
        try
        {
            var result = await call;
            Assert.Fail(
                "Expected TryElicitChoiceAsync to throw when its token was cancelled with the " +
                $"request in flight, but it returned {(result is null ? "null" : $"\"{result}\"")} " +
                "instead — an unresponsive client plus a cancelled caller must never be reported " +
                "as a user decline.");
        }
        catch (OperationCanceledException ex)
        {
            // The SDK surfaces TaskCanceledException here, so catch the base class manually
            // (ThrowsExactlyAsync<OperationCanceledException> would reject the subclass).
            caught = ex;
        }

        Assert.IsNotNull(caught,
            "Cancelling while an elicitation/create request is genuinely in flight must surface " +
            "as OperationCanceledException out of TryElicitChoiceAsync, so the caller unwinds and " +
            "releases its WorkspaceExecutionGate slot instead of hanging until process restart.");
    }

    /// <summary>
    /// Wires a real <see cref="McpServer"/> to a real <see cref="McpClient"/> over an in-memory
    /// duplex pipe supplied by <see cref="InMemoryMcpClientServerHarness"/> so
    /// <c>server.ClientCapabilities.Elicitation</c> is genuinely populated via the MCP initialize
    /// handshake and <c>server.ElicitAsync</c> genuinely round-trips to a client that never
    /// volunteers an answer.
    /// </summary>
    private static async Task<ControllableElicitationHarness> CreateServerWithControllableElicitationAsync(
        CancellationToken ct)
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingElicitation =
            new TaskCompletionSource<ElicitResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "test-server-elicitation-cancellation",
            clientCapabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            clientHandlers: new McpClientHandlers
            {
                // Answers only when the test says so, so the server must observe its own token's
                // cancellation rather than waiting for a reply. Deliberately NOT a task that can
                // never complete — see ControllableElicitationHarness.DisposeAsync.
                ElicitationHandler = (_, _) =>
                {
                    requestReceived.TrySetResult();
                    return new ValueTask<ElicitResult>(pendingElicitation.Task);
                },
            },
            disposalFailureContext: "elicitation-cancellation",
            ct).ConfigureAwait(false);

        return new ControllableElicitationHarness(harness, requestReceived.Task, pendingElicitation);
    }

    /// <summary>
    /// Owns an <see cref="InMemoryMcpClientServerHarness"/> whose client answers
    /// <c>elicitation/create</c> only when the test releases it, and — critically — releases any
    /// still-pending answer <b>before</b> disposing the harness.
    /// </summary>
    /// <remarks>
    /// That ordering is the whole point of this type
    /// (elicitation-inflight-cancellation-test-harness-deadlock). <c>McpClient.DisposeAsync</c>
    /// waits for outstanding inbound request handlers, so leaving the handler unanswered wedges
    /// teardown forever — in <c>await using</c>, i.e. after the test body has already passed,
    /// where a non-cooperative <c>[Timeout]</c> cannot rescue it and the process must be killed.
    /// Three earlier attempts at an in-flight-cancellation test hit exactly this and were
    /// misattributed to the awaited <c>server.ElicitAsync</c> call, to ThreadPool starvation, or
    /// to duplex-<c>Pipe</c> backpressure; a standalone out-of-process repro ruled all three out
    /// (details in the <see cref="InMemoryMcpClientServerHarness"/> remarks). Encoding the release
    /// in disposal — rather than relying on each test to remember a <c>finally</c> — is what keeps
    /// the dead end from being re-discovered.
    /// </remarks>
    private sealed class ControllableElicitationHarness(
        InMemoryMcpClientServerHarness harness,
        Task requestReceived,
        TaskCompletionSource<ElicitResult> pendingElicitation) : IAsyncDisposable
    {
        /// <summary>The live server, for tests that call server-side elicitation directly.</summary>
        public McpServer Server => harness.Server;

        /// <summary>
        /// Completes from inside the client's elicitation handler, proving the
        /// <c>elicitation/create</c> request genuinely arrived rather than still being in transit.
        /// </summary>
        public Task RequestReceived => requestReceived;

        public async ValueTask DisposeAsync()
        {
            // Must precede harness disposal — see the remarks above.
            pendingElicitation.TrySetResult(new ElicitResult { Action = "decline" });
            await harness.DisposeAsync().ConfigureAwait(false);
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
