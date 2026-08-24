using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct coverage for <see cref="StructuredCallElicitationCoordinator"/>, the elicitation/retry
/// orchestration layer extracted from <see cref="StructuredCallToolFilter"/>. These tests call the
/// coordinator directly so the extracted collaborator is exercised on its own surface. Live
/// filter composition and transport behavior are pinned by <c>WorkspacePathMrtrWireTests</c>.
/// </summary>
[TestClass]
public sealed class StructuredCallElicitationCoordinatorTests
{
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TryRecoverMissingWorkspacePathAsync_CancelledWhenDispatchCompletes_DoesNotReturnSuccess()
    {
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "workspace-path-post-dispatch-cancellation",
            clientCapabilities: capabilities,
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "workspace-path-post-dispatch-cancellation",
            cancellationToken: CancellationToken.None,
            protocolVersion: null);
        using var cts = new CancellationTokenSource();
        var dispatchCount = 0;
        var context = new RequestContext<CallToolRequestParams>(
            harness.Server,
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Context = new JsonRpcMessageContext
                {
                    ProtocolVersion = RequestProtocolFeatureGate.July2026ProtocolVersion,
                    ClientCapabilities = capabilities,
                },
            },
            new CallToolRequestParams
            {
                Name = ElicitationAllowlistPolicy.WorkspaceLoadToolName,
                InputResponses = new Dictionary<string, InputResponse>(StringComparer.Ordinal)
                {
                    [RequestScopedInputAdapter.WorkspacePathInputRequestKey] =
                        InputResponse.FromElicitResult(new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                [ElicitationAllowlistPolicy.PathParameterName] =
                                    JsonSerializer.SerializeToElement("C:/repo/SampleSolution.slnx"),
                            },
                        }),
                },
            });

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspacePathAsync(
                context,
                (_, _) =>
                {
                    dispatchCount++;
                    cts.Cancel();
                    return ValueTask.FromResult(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "nominal-success" }],
                    });
                },
                logger: null,
                cancellationToken: cts.Token));

        Assert.AreEqual(1, dispatchCount,
            "The non-vacuous regression must reach the accepted workspace_load dispatch exactly once.");
    }

    [TestMethod]
    public void RecoveryFailureLogging_EmitsTypeOnly_WithoutRawExceptionDetail()
    {
        const string sentinel = "recovery-secret-sentinel";
        const string privatePath = "C:/private/tenant/solution.slnx";
        var logger = new ListLogger<StructuredCallElicitationCoordinatorTests>();

        StructuredCallElicitationCoordinator.LogRecoveryFailure(
            logger,
            new InvalidOperationException($"{sentinel} at {privatePath}"),
            "workspace_load dispatch");

        Assert.HasCount(1, logger.Entries);
        var entry = logger.Entries[0];
        Assert.IsNull(entry.Exception, "Expected recovery failures must not attach raw exception detail to logs.");
        StringAssert.Contains(entry.Message, nameof(InvalidOperationException));
        Assert.IsFalse(entry.Message.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(entry.Message.Contains(privatePath, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("direct-path-input")]
    [DataRow("workspace-id-input")]
    [DataRow("workspace-load-dispatch")]
    [DataRow("retry-dispatch")]
    public async Task RecoveryAwaitBoundary_OperationCanceledException_PropagatesUnchanged(string phase)
    {
        var expected = new OperationCanceledException(phase);
        Task InvokeAsync()
        {
            if (phase == "direct-path-input")
            {
                return StructuredCallElicitationCoordinator.TryRunRecoveryStepAsync<CallToolResult>(
                    () => ValueTask.FromException<CallToolResult>(expected),
                    _ => Assert.Fail("Cancellation reached the recovery-failure callback.")).AsTask();
            }

            return StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
                "workspace_status",
                originalArguments: null,
                elicitAsync: _ => phase == "workspace-id-input"
                    ? ValueTask.FromException<ElicitResult>(expected)
                    : ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["path"] = JsonSerializer.SerializeToElement("C:/repo/SampleSolution.slnx"),
                        },
                    }),
                dispatchAsync: (toolName, _) =>
                {
                    if ((phase == "workspace-load-dispatch" && toolName == "workspace_load")
                        || (phase == "retry-dispatch" && toolName == "workspace_status"))
                    {
                        return Task.FromException<CallToolResult>(expected);
                    }

                    return Task.FromResult(new CallToolResult
                    {
                        Content =
                        [
                            new TextContentBlock
                            {
                                Text = JsonSerializer.Serialize(
                                    new { WorkspaceId = "ws-recovered" },
                                    JsonDefaults.Indented),
                            },
                        ],
                    });
                },
                logger: null,
                cancellationToken: CancellationToken.None);
        }

        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(InvokeAsync);

        Assert.AreSame(expected, actual, $"{phase} must preserve the original cancellation instance.");
    }

    [TestMethod]
    public async Task TryRunRecoveryStepAsync_OrdinaryFailure_ReturnsFailedAttempt()
    {
        var expected = new InvalidOperationException("ordinary recovery failure");
        Exception? observed = null;

        var attempt = await StructuredCallElicitationCoordinator.TryRunRecoveryStepAsync<CallToolResult>(
            () => ValueTask.FromException<CallToolResult>(expected),
            ex => observed = ex);

        Assert.IsFalse(attempt.Succeeded);
        Assert.IsNull(attempt.Value);
        Assert.AreSame(expected, observed);
    }

    [TestMethod]
    public async Task TryRecoverMissingWorkspaceIdAsync_ElicitsPathLoadsWorkspaceAndRetriesOriginalTool()
    {
        const string solutionPath = "C:/repo/SampleSolution.slnx";
        const string recoveredWorkspaceId = "ws-recovered";
        var elicitationCount = 0;
        var dispatches = new List<(string ToolName, IReadOnlyDictionary<string, JsonElement> Arguments)>();

        var result = await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
            "workspace_status",
            originalArguments: null,
            elicitAsync: request =>
            {
                elicitationCount++;
                Assert.IsTrue(request.RequestedSchema!.Properties.ContainsKey("path"),
                    "Missing workspaceId recovery must elicit workspace_load.path, not ask the user to invent a session id.");
                Assert.IsTrue(request.Message.Contains("workspaceId", StringComparison.Ordinal));

                var pathElement = JsonSerializer.SerializeToElement(solutionPath, JsonDefaults.Indented);
                return ValueTask.FromResult(new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement> { ["path"] = pathElement },
                });
            },
            dispatchAsync: (toolName, arguments) =>
            {
                dispatches.Add((toolName, new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal)));

                if (toolName == "workspace_load")
                {
                    Assert.AreEqual(solutionPath, arguments["path"].GetString());
                    return Task.FromResult(new CallToolResult
                    {
                        Content =
                        [
                            new TextContentBlock
                            {
                                Text = JsonSerializer.Serialize(
                                    new { WorkspaceId = recoveredWorkspaceId }, JsonDefaults.Indented),
                            },
                        ],
                    });
                }

                Assert.AreEqual("workspace_status", toolName);
                Assert.AreEqual(recoveredWorkspaceId, arguments["workspaceId"].GetString());
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = """{"state":"ready"}""" }],
                });
            },
            logger: null,
            cancellationToken: CancellationToken.None);

        Assert.IsNotNull(result, "A supporting client path elicitation should recover and retry.");
        Assert.AreEqual(1, elicitationCount, "The recovery path should ask the user exactly once.");
        CollectionAssert.AreEqual(
            new[] { "workspace_load", "workspace_status" },
            dispatches.Select(dispatch => dispatch.ToolName).ToArray(),
            "Recovery must load the workspace before retrying the original workspace-scoped tool.");
        Assert.AreEqual("""{"state":"ready"}""", ((TextContentBlock)result.Content![0]).Text);
    }

    [TestMethod]
    public async Task TryRecoverMissingWorkspaceIdAsync_CancelledWithAcceptedResponse_DoesNotDispatch()
    {
        using var cts = new CancellationTokenSource();
        var dispatchCount = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
                "workspace_status",
                originalArguments: null,
                elicitAsync: _ =>
                {
                    cts.Cancel();
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["path"] = JsonSerializer.SerializeToElement("C:/repo/SampleSolution.slnx"),
                        },
                    });
                },
                dispatchAsync: (_, _) =>
                {
                    dispatchCount++;
                    return Task.FromResult(new CallToolResult());
                },
                logger: null,
                cancellationToken: cts.Token));

        Assert.AreEqual(0, dispatchCount,
            "Cancellation arriving with an accepted response must stop before workspace_load mutates session state.");
    }

    [TestMethod]
    public async Task TryRecoverMissingWorkspaceIdAsync_CancelledWhenLoadCompletes_DoesNotRetryOriginalTool()
    {
        using var cts = new CancellationTokenSource();
        var dispatches = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
                "workspace_status",
                originalArguments: null,
                elicitAsync: _ => ValueTask.FromResult(new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement>
                    {
                        ["path"] = JsonSerializer.SerializeToElement("C:/repo/SampleSolution.slnx"),
                    },
                }),
                dispatchAsync: (toolName, _) =>
                {
                    dispatches.Add(toolName);
                    Assert.AreEqual("workspace_load", toolName,
                        "The original tool must not be retried after cancellation.");
                    cts.Cancel();
                    return Task.FromResult(new CallToolResult
                    {
                        Content =
                        [
                            new TextContentBlock
                            {
                                Text = JsonSerializer.Serialize(new { WorkspaceId = "ws-recovered" }),
                            },
                        ],
                    });
                },
                logger: null,
                cancellationToken: cts.Token));

        CollectionAssert.AreEqual(new[] { "workspace_load" }, dispatches);
    }

    [TestMethod]
    public async Task TryRecoverMissingWorkspaceIdAsync_UserDeclines_ReturnsNull()
    {
        var result = await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
            "workspace_status",
            originalArguments: null,
            elicitAsync: _ => ValueTask.FromResult(new ElicitResult { Action = "decline" }),
            dispatchAsync: (_, _) =>
            {
                Assert.Fail("Dispatch must not run when the user declines the recovery elicitation.");
                return Task.FromResult(new CallToolResult());
            },
            logger: null,
            cancellationToken: CancellationToken.None);

        Assert.IsNull(result, "A declined elicitation must fall through to the existing envelope (null).");
    }

    [TestMethod]
    public async Task TryRecoverMissingWorkspaceIdAsync_WorkspaceLoadDispatchThrows_ReturnsNullWithoutEscaping()
    {
        const string solutionPath = "C:/repo/SampleSolution.slnx";

        var result = await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
            "workspace_status",
            originalArguments: null,
            elicitAsync: _ =>
            {
                var pathElement = JsonSerializer.SerializeToElement(solutionPath, JsonDefaults.Indented);
                return ValueTask.FromResult(new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement> { ["path"] = pathElement },
                });
            },
            dispatchAsync: (toolName, _) =>
            {
                Assert.AreEqual("workspace_load", toolName,
                    "A throw from the workspace_load dispatch must stop recovery before retrying the original tool.");
                throw new InvalidOperationException("workspace_load blew up");
            },
            logger: null,
            cancellationToken: CancellationToken.None);

        Assert.IsNull(result,
            "A throwing workspace_load dispatch must be caught and surface as a null fall-through, not escape.");
    }

    [TestMethod]
    public async Task TryRecoverMissingWorkspaceIdAsync_RetriedToolDispatchThrows_PropagatesToOwningFilter()
    {
        const string solutionPath = "C:/repo/SampleSolution.slnx";
        const string recoveredWorkspaceId = "ws-recovered";
        var originalDispatchCount = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
                "workspace_status",
                originalArguments: null,
                elicitAsync: _ =>
                {
                    var pathElement = JsonSerializer.SerializeToElement(solutionPath, JsonDefaults.Indented);
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement> { ["path"] = pathElement },
                    });
                },
                dispatchAsync: (toolName, _) =>
                {
                    if (toolName == "workspace_load")
                    {
                        return Task.FromResult(new CallToolResult
                        {
                            Content =
                            [
                                new TextContentBlock
                                {
                                    Text = JsonSerializer.Serialize(
                                        new { WorkspaceId = recoveredWorkspaceId }, JsonDefaults.Indented),
                                },
                            ],
                        });
                    }

                    Assert.AreEqual("workspace_status", toolName);
                    originalDispatchCount++;
                    throw new InvalidOperationException("retried tool blew up");
                },
                logger: null,
                cancellationToken: CancellationToken.None));

        Assert.AreEqual("retried tool blew up", exception.Message);
        Assert.AreEqual(1, originalDispatchCount,
            "The coordinator must dispatch the retried original tool exactly once before its failure escapes.");
    }

}
