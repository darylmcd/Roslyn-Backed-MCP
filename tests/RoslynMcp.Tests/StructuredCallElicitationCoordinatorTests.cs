using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct coverage for <see cref="StructuredCallElicitationCoordinator"/>, the elicitation/retry
/// orchestration layer extracted from <see cref="StructuredCallToolFilter"/> by the
/// <c>structuredcalltoolfilter-hotspot-decomposition-followup</c> initiative. These tests call the
/// coordinator directly (not through the filter's thin delegate) so the extracted collaborator is
/// exercised on its own surface. The delegate-forwarded behavior stays pinned by
/// <see cref="StructuredCallToolFilterElicitationTests"/>.
///
/// <para>
/// The recover-load-retry loop takes its elicit + dispatch collaborators as delegates, so it is
/// fully unit-testable without standing up a live MCP transport. <see cref="McpServer"/>-bound
/// entry points (<c>TryElicitAndRetryAsync</c>, the transport-driven arms of
/// <see cref="ElicitationChoicePrompt.TryElicitChoiceAsync"/>) still require a real server; their
/// gate logic is covered via the null-short-circuit and the allowlist tests in
/// <see cref="StructuredCallToolFilterElicitationTests"/>. The picker itself is not a coordinator
/// member — its canonical home is <see cref="ElicitationChoicePrompt"/>; the null-server
/// short-circuit is pinned here for historical continuity with this suite.
/// </para>
/// </summary>
[TestClass]
public sealed class StructuredCallElicitationCoordinatorTests
{
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
    public async Task TryRecoverMissingWorkspaceIdAsync_RetriedToolDispatchThrows_ReturnsNullWithoutEscaping()
    {
        const string solutionPath = "C:/repo/SampleSolution.slnx";
        const string recoveredWorkspaceId = "ws-recovered";

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
                throw new InvalidOperationException("retried tool blew up");
            },
            logger: null,
            cancellationToken: CancellationToken.None);

        Assert.IsNull(result,
            "A throwing retried-tool dispatch must be caught and surface as a null fall-through, not escape.");
    }

    [TestMethod]
    public async Task TryElicitChoiceAsync_NullServer_ReturnsNull()
    {
        // The disambiguation picker short-circuits to null when there is no connected server,
        // so a caller with no elicitation-capable client falls through to its additive list.
        var result = await ElicitationChoicePrompt.TryElicitChoiceAsync(
            server: null,
            paramName: "choice",
            title: "Pick a symbol",
            description: "Multiple symbols matched.",
            options: [("k1", "Label 1"), ("k2", "Label 2")],
            cancellationToken: CancellationToken.None);

        Assert.IsNull(result, "A null server must short-circuit the picker to null.");
    }
}
