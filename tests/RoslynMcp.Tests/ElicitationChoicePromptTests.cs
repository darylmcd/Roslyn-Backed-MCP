using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ElicitationChoicePromptTests
{

    [TestMethod]
    public void ExpectedFailureLogging_EmitsTypeOnly_WithoutRawExceptionDetail()
    {
        const string sentinel = "elicitation-secret-sentinel";
        const string privatePath = "C:/private/tenant/solution.slnx";
        var logger = new ListLogger<ElicitationChoicePromptTests>();

        ElicitationChoicePrompt.LogExpectedFailure(
            logger,
            new InvalidOperationException($"{sentinel} at {privatePath}"));

        Assert.HasCount(1, logger.Entries);
        var entry = logger.Entries[0];
        Assert.IsNull(entry.Exception, "Expected SDK failures must not attach raw exception detail to logs.");
        StringAssert.Contains(entry.Message, nameof(InvalidOperationException));
        Assert.IsFalse(entry.Message.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(entry.Message.Contains(privatePath, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void HasElicitation_CapabilityMatrix_IsOwnedHere()
    {
        Assert.IsTrue(ElicitationChoicePrompt.HasElicitation(
            new ClientCapabilities { Elicitation = new ElicitationCapability() }));
        Assert.IsTrue(ElicitationChoicePrompt.HasElicitation(
            new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Form = new FormElicitationCapability(),
                },
            }));
        Assert.IsFalse(ElicitationChoicePrompt.HasElicitation(
            new ClientCapabilities
            {
                Elicitation = new ElicitationCapability
                {
                    Url = new UrlElicitationCapability(),
                },
            }), "URL-only elicitation cannot satisfy an in-band form request.");
        Assert.IsFalse(ElicitationChoicePrompt.HasElicitation(new ClientCapabilities()));
        Assert.IsFalse(ElicitationChoicePrompt.HasElicitation(null));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task SupportsElicitation_UsesRequestCapabilitiesForModern_AndServerSnapshotForLegacy()
    {
        var formCapabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };

        await using var modernHarness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "choice-modern-capability-source",
            clientCapabilities: formCapabilities,
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "choice-modern-capability-source",
            cancellationToken: CancellationToken.None,
            protocolVersion: null);
        Assert.IsNull(modernHarness.Server.ClientCapabilities,
            "The modern root server has no session-scoped capabilities; stateless transports have the same shape.");
        var modernContext = CreateRequestContext(
            modernHarness.Server,
            RequestProtocolFeatureGate.July2026ProtocolVersion,
            formCapabilities);
        Assert.IsTrue(ElicitationChoicePrompt.SupportsElicitation(modernContext),
            "Modern capability gating must read the request metadata when server state is null.");

        await using var legacyHarness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "choice-legacy-capability-source",
            clientCapabilities: formCapabilities,
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "choice-legacy-capability-source",
            cancellationToken: CancellationToken.None,
            protocolVersion: "2025-11-25");
        Assert.IsNotNull(legacyHarness.Server.ClientCapabilities);
        var modernWithoutRequestCapabilities = CreateRequestContext(
            legacyHarness.Server,
            RequestProtocolFeatureGate.July2026ProtocolVersion,
            capabilities: null);
        Assert.IsFalse(ElicitationChoicePrompt.SupportsElicitation(modernWithoutRequestCapabilities),
            "Modern requests must fail closed instead of falling back to stale server capabilities.");
        var legacyContext = CreateRequestContext(
            legacyHarness.Server,
            protocolVersion: "2025-11-25",
            capabilities: null);
        Assert.IsTrue(ElicitationChoicePrompt.SupportsElicitation(legacyContext),
            "Legacy initialize-handshake requests retain the server capability snapshot.");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TryElicitChoiceAsync_LegacyInjectedInputResponse_IsIgnoredForNestedElicitation()
    {
        const string injectedChoice = "injected";
        const string trustedChoice = "trusted";
        var nestedElicitationCount = 0;
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "choice-legacy-injected-response",
            clientCapabilities: new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
            },
            clientHandlers: new McpClientHandlers
            {
                ElicitationHandler = (_, _) =>
                {
                    Interlocked.Increment(ref nestedElicitationCount);
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["choice"] = JsonSerializer.SerializeToElement(trustedChoice),
                        },
                    });
                },
            },
            disposalFailureContext: "choice-legacy-injected-response",
            cancellationToken: CancellationToken.None,
            protocolVersion: "2025-11-25");
        var context = CreateRequestContext(
            harness.Server,
            protocolVersion: "2025-11-25",
            capabilities: null,
            inputResponses: new Dictionary<string, InputResponse>
            {
                [RequestScopedInputAdapter.SymbolChoiceInputRequestKey] =
                    InputResponse.FromElicitResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["choice"] = JsonSerializer.SerializeToElement(injectedChoice),
                        },
                    }),
            });

        var choice = await ElicitationChoicePrompt.TryElicitChoiceAsync(
            context,
            "choice",
            "Pick a symbol",
            "Multiple symbols matched.",
            [(injectedChoice, "Injected"), (trustedChoice, "Trusted")],
            CancellationToken.None);

        Assert.AreEqual(1, nestedElicitationCount,
            "Legacy requests must use the stateful nested elicitation leg even if inputResponses is injected.");
        Assert.AreEqual(trustedChoice, choice,
            "A newer-protocol inputResponses member must not bypass legacy elicitation.");
    }

    [TestMethod]
    public async Task TryElicitChoiceAsync_NullContext_ReturnsNull()
    {
        var result = await ElicitationChoicePrompt.TryElicitChoiceAsync(
            context: null,
            paramName: "choice",
            title: "Pick a symbol",
            description: "Multiple symbols matched.",
            options: [("k1", "Label 1"), ("k2", "Label 2")],
            cancellationToken: CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task TryElicitChoiceCoreAsync_ActualElicitAsyncUnsupportedFailure_ReturnsNull()
    {
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "choice-unsupported-client",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "choice-unsupported-client",
            cancellationToken: CancellationToken.None,
            protocolVersion: "2025-11-25");

        Exception? observed = null;
        async ValueTask<(RequestScopedInputOutcome, ElicitResult?)> RequestAsync(
            ElicitRequestParams request,
            CancellationToken token)
        {
            try
            {
                return await RequestLegacyElicitationAsync(harness.Server, request, token);
            }
            catch (Exception ex)
            {
                observed = ex;
                throw;
            }
        }

        var result = await ElicitationChoicePrompt.TryElicitChoiceCoreAsync(
            "choice",
            "Pick a symbol",
            "Multiple symbols matched.",
            [("k1", "Label 1"), ("k2", "Label 2")],
            RequestAsync,
            onExpectedFailure: null,
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsInstanceOfType<InvalidOperationException>(observed,
            "A real McpServer.ElicitAsync call must produce the documented unsupported-client shape.");
    }

    [TestMethod]
    public async Task TryElicitChoiceAsync_ActualElicitAsyncClientError_ReturnsNull()
    {
        var handlerReached = false;
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "choice-client-error",
            clientCapabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            clientHandlers: new McpClientHandlers
            {
                ElicitationHandler = (_, _) =>
                {
                    handlerReached = true;
                    return ValueTask.FromException<ElicitResult>(
                        new McpException("synthetic client error"));
                },
            },
            disposalFailureContext: "choice-client-error",
            cancellationToken: CancellationToken.None,
            protocolVersion: "2025-11-25");

        var context = CreateRequestContext(
            harness.Server,
            protocolVersion: "2025-11-25",
            capabilities: null);
        var result = await ElicitationChoicePrompt.TryElicitChoiceAsync(
            context,
            "choice",
            "Pick a symbol",
            "Multiple symbols matched.",
            [("k1", "Label 1"), ("k2", "Label 2")],
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsTrue(handlerReached,
            "The public wrapper must reach the real legacy server.ElicitAsync boundary before falling back.");
    }

    [TestMethod]
    public async Task TryElicitChoiceCoreAsync_RejectsOversizedOrStaleChoices()
    {
        var requestCount = 0;
        var oversized = Enumerable.Range(0, ElicitationChoicePrompt.MaxOptions + 1)
            .Select(index => ($"k{index}", $"Label {index}"))
            .ToArray();

        var capped = await ElicitationChoicePrompt.TryElicitChoiceCoreAsync(
            "choice", "Pick", "Pick one.", oversized,
            (_, _) =>
            {
                requestCount++;
                return ValueTask.FromResult((
                    RequestScopedInputOutcome.Accepted,
                    (ElicitResult?)null));
            },
            onExpectedFailure: null,
            CancellationToken.None);
        Assert.IsNull(capped);
        Assert.AreEqual(0, requestCount, "The option cap must be enforced before any input request is sent.");

        var stale = await ElicitationChoicePrompt.TryElicitChoiceCoreAsync(
            "choice", "Pick", "Pick one.", [("current", "Current")],
            (_, _) => ValueTask.FromResult((
                RequestScopedInputOutcome.Accepted,
                (ElicitResult?)new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement>
                    {
                        ["choice"] = JsonSerializer.SerializeToElement("stale"),
                    },
                })),
            onExpectedFailure: null,
            CancellationToken.None);
        Assert.IsNull(stale, "A response key outside the current candidate set must preserve the additive-list fallback.");
    }

    [TestMethod]
    public async Task TryElicitChoiceCoreAsync_CancelledAsAcceptedResponseReturns_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var responseReached = false;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ElicitationChoicePrompt.TryElicitChoiceCoreAsync(
                "choice",
                "Pick",
                "Pick one.",
                [("current", "Current")],
                (_, _) =>
                {
                    responseReached = true;
                    cts.Cancel();
                    return ValueTask.FromResult((
                        RequestScopedInputOutcome.Accepted,
                        (ElicitResult?)new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement>
                            {
                                ["choice"] = JsonSerializer.SerializeToElement("current"),
                            },
                        }));
                },
                onExpectedFailure: null,
                cts.Token));

        Assert.IsTrue(responseReached,
            "Cancellation must be observed after a real accepted-response leg, not only before dispatch.");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TryElicitChoiceAsync_PreCancelledHandCraftedMrtrRetry_DoesNotConsumeAcceptedChoice()
    {
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "choice-cancelled-mrtr-retry",
            clientCapabilities: capabilities,
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "choice-cancelled-mrtr-retry",
            cancellationToken: CancellationToken.None,
            protocolVersion: null);
        var context = CreateRequestContext(
            harness.Server,
            RequestProtocolFeatureGate.July2026ProtocolVersion,
            capabilities,
            new Dictionary<string, InputResponse>
            {
                [RequestScopedInputAdapter.SymbolChoiceInputRequestKey] =
                    InputResponse.FromElicitResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["choice"] = JsonSerializer.SerializeToElement("current"),
                        },
                    }),
            });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ElicitationChoicePrompt.TryElicitChoiceAsync(
                context,
                "choice",
                "Pick",
                "Pick one.",
                [("current", "Current")],
                cts.Token));
    }

    // ── cancellation must propagate, not be reported as decline ─────────────
    // elicitation-trychoice-cancellation-swallow: the choice core's try/catch around
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
    // The blocking point was never TryElicitChoiceCoreAsync or server.ElicitAsync; it was the old
    // never-completing ElicitationHandler wedging McpClient.DisposeAsync during `await using`
    // teardown, AFTER the assertions had already passed. ControllableElicitationHarness fixes
    // that by owning a completable handler; see its remarks and those on
    // InMemoryMcpClientServerHarness for the full ruled-in/ruled-out evidence.

    [TestMethod]
    [Timeout(15_000, CooperativeCancellation = true)]
    public async Task TryElicitChoiceCoreAsync_PreCancelledToken_PropagatesCancellation_NotAdditiveListFallback()
    {
        // A real McpServer/McpClient pair over the shared in-memory duplex harness exercises the
        // core delegate against the actual legacy McpServer.ElicitAsync boundary. The token below
        // is cancelled BEFORE the call, so the request never reaches the client's handler; the
        // in-flight variant is the next test.
        await using var harness = await CreateServerWithControllableElicitationAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        OperationCanceledException? caught = null;
        try
        {
            var result = await ElicitationChoicePrompt.TryElicitChoiceCoreAsync(
                "choice",
                "Pick a symbol",
                "symbol_search returned candidates. Pick one to focus on.",
                [("0", "Candidate A"), ("1", "Candidate B")],
                (request, token) => RequestLegacyElicitationAsync(harness.Server, request, token),
                onExpectedFailure: null,
                cts.Token);

            Assert.Fail(
                "Expected TryElicitChoiceCoreAsync to throw for a pre-cancelled token, but it " +
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
            "of TryElicitChoiceCoreAsync, not be swallowed by the InvalidOperationException/" +
            "McpException catch and converted to the additive-list null fallback.");
    }

    [TestMethod]
    [Timeout(15_000, CooperativeCancellation = true)]
    public async Task TryElicitChoiceCoreAsync_CancelledWhileRequestInFlight_PropagatesCancellation()
    {
        // The non-vacuous variant of the test above: the elicitation/create request genuinely
        // reaches the client (RequestReceived below only completes from inside the client's own
        // handler), the client then never answers, and only THEN is the caller's token cancelled.
        // This pins that a real MCP client going unresponsive mid-elicitation cannot wedge the
        // server: the await unblocks and the caller's WorkspaceExecutionGate slot is released.
        await using var harness = await CreateServerWithControllableElicitationAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();

        var call = ElicitationChoicePrompt.TryElicitChoiceCoreAsync(
            "choice",
            "Pick a symbol",
            "symbol_search returned candidates. Pick one to focus on.",
            [("0", "Candidate A"), ("1", "Candidate B")],
            (request, token) => RequestLegacyElicitationAsync(harness.Server, request, token),
            onExpectedFailure: null,
            cts.Token);

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
            cancellationToken: ct,
            protocolVersion: "2025-11-25").ConfigureAwait(false);

        return new ControllableElicitationHarness(harness, requestReceived.Task, pendingElicitation);
    }

    private static async ValueTask<(RequestScopedInputOutcome, ElicitResult?)> RequestLegacyElicitationAsync(
        McpServer server,
        ElicitRequestParams request,
        CancellationToken cancellationToken)
    {
        var result = await server.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
        return (result.IsAccepted
            ? RequestScopedInputOutcome.Accepted
            : RequestScopedInputOutcome.DeclinedOrCancelled, result);
    }

    private static RequestContext<CallToolRequestParams> CreateRequestContext(
        McpServer server,
        string protocolVersion,
        ClientCapabilities? capabilities,
        IDictionary<string, InputResponse>? inputResponses = null) =>
        new(
            server,
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Context = new JsonRpcMessageContext
                {
                    ProtocolVersion = protocolVersion,
                    ClientCapabilities = capabilities,
                },
            },
            new CallToolRequestParams
            {
                Name = "symbol_search",
                InputResponses = inputResponses,
            });

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


}
