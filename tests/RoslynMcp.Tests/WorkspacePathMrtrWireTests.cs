using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Raw-wire contract for the <c>mcp-mrtr-dispatch-contract</c> initiative: server-driven input
/// through <see cref="RequestScopedInputAdapter"/> must be portable across both SDK 2.1 session
/// eras. Mirrors the dual-protocol harness pattern of
/// <see cref="ProtocolVersionResultShapeWireTests"/> (negotiated <c>2025-11-25</c> vs
/// <c>2026-07-28</c>, asserting against <c>harness.RawServerMessages</c>). Pins:
/// <list type="bullet">
///   <item><b>MRTR round trip</b> — under <c>2026-07-28</c>, a tool call missing an allowlisted
///   parameter yields an <c>input_required</c> result on the wire (NOT an <c>isError</c>
///   envelope, which was defect (1): the filter's <c>catch (Exception)</c> used to swallow
///   <see cref="InputRequiredException"/>); the SDK client resolves the embedded elicitation and
///   the retry carrying <c>params.inputResponses</c> completes with a success envelope.</item>
///   <item><b>Stateful compatibility</b> — under <c>2025-11-25</c>, the direct nested
///   <c>elicitation/create</c> leg still round-trips and the final result keeps the legacy shape
///   (no <c>resultType</c> discriminator).</item>
///   <item><b>Sanitized non-accept outcomes</b> — declined and malformed input responses each
///   fall through to the existing schema-hint <c>InvalidArgument</c> envelope with no raw
///   exception text or payload echo.</item>
///   <item><b>Cancellation</b> — cancelling while the adapter's legacy elicitation leg is in
///   flight surfaces as protocol cancellation, never as a tool-error envelope.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WorkspacePathMrtrWireTests
{
    // The fake tool deliberately reuses the workspace_load name so the recovery pipeline's
    // strict allowlist (workspace_load.path) permits elicitation for the missing parameter.
    private const string ToolName = "workspace_load";
    private const string WorkspaceStatusToolName = "workspace_status";
    private const string ElicitedPath = "C:/synthetic/solution.slnx";
    private const string SyntheticWorkspaceId = "11111111111111111111111111111111";

    // ── (1) MRTR round trip under 2026-07-28 ─────────────────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_MissingPath_EmitsInputRequiredResult_ThenRetryCompletes()
    {
        var elicitationsHandled = 0;
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) =>
            {
                Interlocked.Increment(ref elicitationsHandled);
                return new ValueTask<ElicitResult>(AcceptedPathResult());
            });
        Assert.AreEqual("2026-07-28", harness.Client.NegotiatedProtocolVersion);

        var prior = harness.RawServerMessages.Count;
        var clientResult = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, elicitationsHandled,
            "The client must resolve exactly one embedded elicitation input request.");
        Assert.IsFalse(clientResult.IsError is true,
            "The retry carrying inputResponses must complete with a success envelope.");

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(2, results);

        // Initial call: an input-required protocol result, not a failed tool call.
        var inputRequired = results[0];
        Assert.AreEqual("input_required", inputRequired.GetProperty("resultType").GetString(),
            "The initial tools/call must terminate in an InputRequiredResult on the wire — an " +
            "isError CallToolResult here means the filter swallowed InputRequiredException.");
        Assert.IsFalse(inputRequired.TryGetProperty("isError", out _));
        var inputRequest = inputRequired
            .GetProperty("inputRequests")
            .GetProperty(RequestScopedInputAdapter.WorkspacePathInputRequestKey);
        Assert.AreEqual("elicitation/create", inputRequest.GetProperty("method").GetString());
        Assert.IsFalse(inputRequired.TryGetProperty("requestState", out _),
            "Missing-path recovery has no resolved workspace identity to preserve on its first " +
            "round trip; later MRTR stages publish request state after a workspace is selected.");

        // Retry: success envelope carrying the elicited value.
        var final = results[1];
        Assert.IsFalse(final.TryGetProperty("isError", out var finalIsError) && finalIsError.GetBoolean());
        StringAssert.Contains(final.GetProperty("content")[0].GetProperty("text").GetString(), ElicitedPath);

        // The MRTR leg must not also send the legacy nested server-to-client request.
        Assert.IsFalse(
            AnyServerRequest(harness.RawServerMessages, prior, RequestMethods.ElicitationCreate),
            "Under MRTR the elicitation rides inside the InputRequiredResult; the server must " +
            "not additionally send a nested elicitation/create request.");
    }

    // ── (2) stateful compatibility under 2025-11-25 ──────────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task LegacySession_MissingPath_UsesDirectElicitation_AndLegacyResultShape()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: "2025-11-25",
            elicitationHandler: (_, _) => new ValueTask<ElicitResult>(AcceptedPathResult()));
        Assert.AreEqual("2025-11-25", harness.Client.NegotiatedProtocolVersion);

        var prior = harness.RawServerMessages.Count;
        var clientResult = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.IsFalse(clientResult.IsError is true);
        Assert.IsTrue(
            AnyServerRequest(harness.RawServerMessages, prior, RequestMethods.ElicitationCreate),
            "A 2025-11-25 session must keep the direct nested elicitation/create recovery leg.");

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(1, results,
            "The stateful leg answers the original request in a single wire result — " +
            "input_required never appears on a legacy session.");
        var final = results[0];
        Assert.IsFalse(final.TryGetProperty("resultType", out _),
            "ApplyProtocolResultShape strips the July 2026 discriminator for legacy sessions.");
        Assert.IsFalse(final.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        StringAssert.Contains(final.GetProperty("content")[0].GetProperty("text").GetString(), ElicitedPath);
    }

    // ── (3) sanitized non-accept outcomes ────────────────────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_DeclinedInputResponse_FallsThroughToSanitizedSchemaHintEnvelope()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) =>
                new ValueTask<ElicitResult>(new ElicitResult { Action = "decline" }));

        var prior = harness.RawServerMessages.Count;
        var clientResult = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(clientResult.IsError is true,
            "A declined input response cannot recover the call; the existing InvalidArgument " +
            "envelope is the contract.");

        var results = FindNewResults(harness.RawServerMessages, prior);
        Assert.HasCount(2, results,
            "Initial input_required round trip, then the retry's terminal error envelope.");
        var final = results[1];
        Assert.IsTrue(final.GetProperty("isError").GetBoolean());
        AssertSanitizedInvalidArgumentEnvelope(final);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_MalformedInputResponse_FallsThroughToSanitizedSchemaHintEnvelope()
    {
        var elicitationsHandled = 0;
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) =>
            {
                Interlocked.Increment(ref elicitationsHandled);
                return new ValueTask<ElicitResult>(AcceptedPathResult());
            });

        // Hand-craft the retry leg: a tools/call already carrying an inputResponses entry whose
        // raw value cannot deserialize into an ElicitResult. The SDK client always sends
        // well-formed responses, so this is the only way to drive the malformed classification
        // over the real wire.
        var prior = harness.RawServerMessages.Count;
        var response = await harness.Client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject
                {
                    ["name"] = ToolName,
                    ["arguments"] = new JsonObject(),
                    ["inputResponses"] = new JsonObject
                    {
                        [RequestScopedInputAdapter.WorkspacePathInputRequestKey] = JsonValue.Create(42),
                    },
                },
            },
            CancellationToken.None);

        Assert.AreEqual(0, elicitationsHandled,
            "A malformed retry response is terminal — the adapter must not restart the flow " +
            "with another input request.");

        var result = JsonSerializer.SerializeToElement(response.Result);
        Assert.IsFalse(result.TryGetProperty("resultType", out var resultType)
                       && resultType.GetString() == "input_required",
            "A malformed response classifies as a sanitized failure, not a fresh input_required.");
        Assert.IsTrue(result.GetProperty("isError").GetBoolean());
        AssertSanitizedInvalidArgumentEnvelope(result);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.IsFalse(text.Contains("JsonException", StringComparison.OrdinalIgnoreCase),
            "Deserialization failure detail must never leak into the envelope.");

        Assert.HasCount(1, FindNewResults(harness.RawServerMessages, prior));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MrtrSession_UnexpectedFormField_IsRefusedWithoutDispatch()
    {
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) => ValueTask.FromResult(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["path"] = JsonSerializer.SerializeToElement(ElicitedPath),
                    ["token"] = JsonSerializer.SerializeToElement("must-not-be-consumed"),
                },
            }));

        var result = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.IsError is true);
        var text = ((TextContentBlock)result.Content![0]).Text;
        Assert.IsFalse(text.Contains("must-not-be-consumed", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task SyntheticTool_UnauthorizedAccess_MapsToSanitizedPermissionDenied()
    {
        const string outsidePath = "C:/outside/private.slnx";
        await using var harness = await CreateHarnessAsync(
            protocolVersion: null,
            elicitationHandler: (_, _) => ValueTask.FromResult(AcceptedPathResult(outsidePath)));

        var result = await harness.Client.CallToolAsync(
            ToolName,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.IsError is true);
        var text = ((TextContentBlock)result.Content![0]).Text;
        Assert.IsFalse(text.Contains(outsidePath, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(text, "PermissionDenied");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ProductionRootValidator_UnsanctionedWorkspacePath_MapsToSanitizedInvalidArgument()
    {
        var sanctionedRoot = Path.Combine(TestTempRoot.Current, "workspace-mrtr-sanctioned");
        Directory.CreateDirectory(sanctionedRoot);
        var outsidePath = Path.Combine(TestTempRoot.Current, "outside-private.slnx");
        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            sanctionedRoot,
            CancellationToken.None,
            useLatestProtocol: true);

        ArgumentException? validationError = null;
        try
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                session.Server,
                outsidePath,
                CancellationToken.None);
            Assert.Fail("The production root validator must reject a workspace path outside its sanctioned root.");
        }
        catch (ArgumentException ex)
        {
            validationError = ex;
        }

        Assert.IsNotNull(validationError);
        var envelope = ToolErrorHandler.ClassifyAndFormat(validationError, ToolName);
        using var document = JsonDocument.Parse(envelope);
        Assert.AreEqual(
            ToolErrorHandler.ErrorCategories.InvalidArgument,
            document.RootElement.GetProperty("category").GetString());
        Assert.IsFalse(envelope.Contains(outsidePath, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(envelope.Contains(
            ToolErrorHandler.ErrorCategories.PermissionDenied,
            StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task WorkspaceIdRecovery_RetriedToolThrows_DispatchesOnceAndReturnsFailureEnvelope()
    {
        const string privateFailureDetail = "retry-private-path:C:/tenant/private.slnx";
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(privateFailureDetail);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) => ValueTask.FromResult(AcceptedPathResult()));

            var prior = harness.RawServerMessages.Count;
            var result = await harness.Client.CallToolAsync(
                WorkspaceStatusToolName,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(result.IsError is true,
                "The owning filter must classify the retried tool failure into one terminal envelope.");
            Assert.AreEqual(1, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount,
                "A failed recovered retry must not fall through and dispatch the original missing arguments again. " +
                $"workspace_load dispatches={SyntheticWorkspaceLoadTools.WorkspaceLoadDispatchCount}; " +
                $"result={((TextContentBlock)result.Content![0]).Text}; wire=" +
                string.Join(" | ", harness.RawServerMessages));
            var text = ((TextContentBlock)result.Content![0]).Text;
            var payload = JsonDocument.Parse(text).RootElement;
            Assert.AreEqual(ToolErrorHandler.ErrorCategories.InvalidOperation,
                payload.GetProperty("category").GetString());
            Assert.AreEqual(WorkspaceStatusToolName, payload.GetProperty("tool").GetString());
            Assert.IsFalse(text.Contains(privateFailureDetail, StringComparison.Ordinal),
                "The classified envelope must not echo raw retry exception detail.");
            var results = FindNewResults(harness.RawServerMessages, prior);
            Assert.HasCount(2, results);
            Assert.AreEqual("input_required", results[0].GetProperty("resultType").GetString());
            Assert.IsTrue(results[1].GetProperty("isError").GetBoolean());
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task WorkspaceIdRecovery_NonStringValue_PreservesBinderInvalidArgumentWithoutRecovery()
    {
        var elicitationCount = 0;
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe("must-not-run");
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) =>
                {
                    Interlocked.Increment(ref elicitationCount);
                    return ValueTask.FromResult(AcceptedPathResult());
                });

            var prior = harness.RawServerMessages.Count;
            var result = await harness.Client.CallToolAsync(
                WorkspaceStatusToolName,
                new Dictionary<string, object?> { ["workspaceId"] = 42 },
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(result.IsError is true);
            Assert.AreEqual(0, elicitationCount,
                "A present but invalid workspaceId is not omission and must never start recovery.");
            Assert.AreEqual(0, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount,
                "The binder must reject the value before the synthetic tool body runs.");
            var results = FindNewResults(harness.RawServerMessages, prior);
            Assert.HasCount(1, results,
                "Malformed explicit input must produce one terminal envelope, not an MRTR exchange.");
            Assert.IsFalse(results[0].TryGetProperty("resultType", out var resultType) &&
                           resultType.GetString() == "input_required");
            var payload = JsonDocument.Parse(((TextContentBlock)result.Content![0]).Text).RootElement;
            Assert.AreEqual(ToolErrorHandler.ErrorCategories.InvalidArgument,
                payload.GetProperty("category").GetString());
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task WorkspacePathRetry_WithConcurrentWorkspaceAndState_UsesAcceptedPathNotAmbientWorkspace()
    {
        var unrelatedWorkspaceId = Guid.NewGuid().ToString("N");
        var manager = new ConfigurableWorkspaceManager(WorkspaceStatus(unrelatedWorkspaceId));
        var elicitationCount = 0;
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(
            failureDetail: null,
            expectedWorkspaceId: SyntheticWorkspaceId);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) =>
                {
                    Interlocked.Increment(ref elicitationCount);
                    return ValueTask.FromResult(AcceptedPathResult());
                },
                manager);
            var responseValue = InputResponse.FromElicitResult(AcceptedPathResult()).RawValue;
            var unrelatedState = RequestStateCodec.CaptureWorkspaceId(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
                        JsonSerializer.SerializeToElement(unrelatedWorkspaceId),
                },
                ElicitationAllowlistPolicy.WorkspaceIdParameterName);

            var response = await harness.Client.SendRequestAsync(
                new JsonRpcRequest
                {
                    Method = RequestMethods.ToolsCall,
                    Params = new JsonObject
                    {
                        ["name"] = WorkspaceStatusToolName,
                        ["arguments"] = new JsonObject(),
                        ["requestState"] = unrelatedState,
                        ["inputResponses"] = new JsonObject
                        {
                            [RequestScopedInputAdapter.WorkspacePathInputRequestKey] =
                                JsonNode.Parse(responseValue.GetRawText()),
                        },
                    },
                },
                CancellationToken.None);

            var result = JsonSerializer.SerializeToElement(response.Result);
            Assert.IsFalse(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
            Assert.AreEqual(0, elicitationCount,
                "The supplied request-scoped response must be consumed without a second prompt.");
            Assert.AreEqual(1, SyntheticWorkspaceLoadTools.WorkspaceLoadDispatchCount,
                "The accepted path must be loaded even when another workspace appeared before retry.");
            Assert.AreEqual(1, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount);
            StringAssert.Contains(result.GetProperty("content")[0].GetProperty("text").GetString(),
                SyntheticWorkspaceId);
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ExplicitWorkspaceId_WinsOverCraftedWorkspacePathResponse()
    {
        var explicitWorkspaceId = Guid.NewGuid().ToString("N");
        var elicitationCount = 0;
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(
            failureDetail: null,
            expectedWorkspaceId: explicitWorkspaceId);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) =>
                {
                    Interlocked.Increment(ref elicitationCount);
                    return ValueTask.FromResult(AcceptedPathResult());
                });
            var responseValue = InputResponse.FromElicitResult(AcceptedPathResult()).RawValue;

            var response = await harness.Client.SendRequestAsync(
                new JsonRpcRequest
                {
                    Method = RequestMethods.ToolsCall,
                    Params = new JsonObject
                    {
                        ["name"] = WorkspaceStatusToolName,
                        ["arguments"] = new JsonObject
                        {
                            [ElicitationAllowlistPolicy.WorkspaceIdParameterName] = explicitWorkspaceId,
                        },
                        ["inputResponses"] = new JsonObject
                        {
                            [RequestScopedInputAdapter.WorkspacePathInputRequestKey] =
                                JsonNode.Parse(responseValue.GetRawText()),
                        },
                    },
                },
                CancellationToken.None);

            var result = JsonSerializer.SerializeToElement(response.Result);
            Assert.IsFalse(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
            Assert.AreEqual(0, elicitationCount);
            Assert.AreEqual(0, SyntheticWorkspaceLoadTools.WorkspaceLoadDispatchCount,
                "A stale/crafted path response must never overwrite an explicit workspaceId.");
            Assert.AreEqual(1, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount);
            StringAssert.Contains(result.GetProperty("content")[0].GetProperty("text").GetString(),
                explicitWorkspaceId);
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task NonAcceptedWorkspacePathResponse_WithValidState_FailsClosed(
        bool malformedResponse)
    {
        var encodedWorkspaceId = Guid.NewGuid().ToString("N");
        var ambientWorkspaceId = Guid.NewGuid().ToString("N");
        var manager = new ConfigurableWorkspaceManager(WorkspaceStatus(ambientWorkspaceId));
        var elicitationCount = 0;
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) =>
                {
                    Interlocked.Increment(ref elicitationCount);
                    return ValueTask.FromResult(AcceptedPathResult());
                },
                manager);
            var state = RequestStateCodec.CaptureWorkspaceId(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
                        JsonSerializer.SerializeToElement(encodedWorkspaceId),
                },
                ElicitationAllowlistPolicy.WorkspaceIdParameterName);
            JsonNode? responseValue = malformedResponse
                ? JsonValue.Create(42)
                : JsonNode.Parse(InputResponse.FromElicitResult(
                    new ElicitResult { Action = "decline" }).RawValue.GetRawText());

            var response = await harness.Client.SendRequestAsync(
                new JsonRpcRequest
                {
                    Method = RequestMethods.ToolsCall,
                    Params = new JsonObject
                    {
                        ["name"] = WorkspaceStatusToolName,
                        ["arguments"] = new JsonObject(),
                        ["requestState"] = state,
                        ["inputResponses"] = new JsonObject
                        {
                            [RequestScopedInputAdapter.WorkspacePathInputRequestKey] = responseValue,
                        },
                    },
                },
                CancellationToken.None);

            var result = JsonSerializer.SerializeToElement(response.Result);
            Assert.IsTrue(result.GetProperty("isError").GetBoolean());
            Assert.AreEqual(0, elicitationCount);
            Assert.AreEqual(0, SyntheticWorkspaceLoadTools.WorkspaceLoadDispatchCount);
            Assert.AreEqual(0, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount,
                "A non-accepted path response must not fall back to request state or ambient state.");
            var payload = JsonDocument.Parse(
                result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
            Assert.AreEqual(ToolErrorHandler.ErrorCategories.InvalidArgument,
                payload.GetProperty("category").GetString());
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task RequestState_PreservesAutoResolvedWorkspaceAcrossMrtrAmbientChange()
    {
        var originalWorkspaceId = Guid.NewGuid().ToString("N");
        var concurrentWorkspaceId = Guid.NewGuid().ToString("N");
        var manager = new ConfigurableWorkspaceManager(WorkspaceStatus(originalWorkspaceId));
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(
            failureDetail: null,
            expectedWorkspaceId: originalWorkspaceId);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) =>
                {
                    manager.ReplaceWith(WorkspaceStatus(concurrentWorkspaceId));
                    return ValueTask.FromResult(AcceptedChoice("first"));
                },
                manager);
            var prior = harness.RawServerMessages.Count;

            var result = await harness.Client.CallToolAsync(
                WorkspaceStatusToolName,
                new Dictionary<string, object?> { ["requestChoice"] = true },
                cancellationToken: CancellationToken.None);

            Assert.IsFalse(result.IsError is true);
            StringAssert.Contains(((TextContentBlock)result.Content![0]).Text, originalWorkspaceId);
            Assert.IsFalse(((TextContentBlock)result.Content[0]).Text.Contains(
                concurrentWorkspaceId,
                StringComparison.Ordinal),
                "The retry must not rebind to ambient workspace state that changed mid-MRTR.");
            var payload = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;
            Assert.AreEqual("request-state",
                payload.GetProperty("_meta").GetProperty("autoResolution").GetString());

            var results = FindNewResults(harness.RawServerMessages, prior);
            Assert.HasCount(2, results);
            var requestState = results[0].GetProperty("requestState").GetString();
            Assert.IsTrue(RequestStateCodec.TryRestoreWorkspaceId(requestState, out var restored));
            Assert.AreEqual(originalWorkspaceId, restored);
            Assert.AreEqual(concurrentWorkspaceId, manager.ListWorkspaces().Single().WorkspaceId,
                "The fixture must genuinely change ambient state before the retry.");
            Assert.AreEqual(2, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount,
                "MRTR re-enters the tool once to publish the choice and once to complete it.");
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task RequestState_PreservesPathRecoveredWorkspaceAcrossSecondMrtrStage()
    {
        var concurrentWorkspaceId = Guid.NewGuid().ToString("N");
        var manager = new ConfigurableWorkspaceManager();
        var promptCount = 0;
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(
            failureDetail: null,
            expectedWorkspaceId: SyntheticWorkspaceId);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: null,
                elicitationHandler: (_, _) =>
                {
                    var prompt = Interlocked.Increment(ref promptCount);
                    if (prompt == 1)
                    {
                        return ValueTask.FromResult(AcceptedPathResult());
                    }

                    manager.ReplaceWith(WorkspaceStatus(concurrentWorkspaceId));
                    return ValueTask.FromResult(AcceptedChoice("first"));
                },
                manager);
            var prior = harness.RawServerMessages.Count;

            var result = await harness.Client.CallToolAsync(
                WorkspaceStatusToolName,
                new Dictionary<string, object?> { ["requestChoice"] = true },
                cancellationToken: CancellationToken.None);

            Assert.IsFalse(result.IsError is true);
            Assert.AreEqual(2, promptCount,
                "The logical call must request a path first and a choice after loading it.");
            Assert.AreEqual(1, SyntheticWorkspaceLoadTools.WorkspaceLoadDispatchCount);
            Assert.AreEqual(2, SyntheticWorkspaceLoadTools.WorkspaceStatusDispatchCount,
                "MRTR re-enters the original tool once for the second input request and once to complete.");
            var text = ((TextContentBlock)result.Content![0]).Text;
            StringAssert.Contains(text, SyntheticWorkspaceId);
            Assert.IsFalse(text.Contains(concurrentWorkspaceId, StringComparison.Ordinal),
                "The second MRTR retry must remain pinned to the path-recovered workspace.");

            var results = FindNewResults(harness.RawServerMessages, prior);
            Assert.HasCount(3, results);
            Assert.IsFalse(results[0].TryGetProperty("requestState", out _),
                "No workspace exists before the path response is accepted.");
            Assert.AreEqual("input_required", results[1].GetProperty("resultType").GetString());
            Assert.IsTrue(results[1].GetProperty("inputRequests").TryGetProperty(
                RequestScopedInputAdapter.SymbolChoiceInputRequestKey,
                out _));
            var state = results[1].GetProperty("requestState").GetString();
            Assert.IsTrue(RequestStateCodec.TryRestoreWorkspaceId(state, out var restored));
            Assert.AreEqual(SyntheticWorkspaceId, restored,
                "Temporary retry arguments must be captured before the coordinator restores them.");
            Assert.AreEqual(concurrentWorkspaceId, manager.ListWorkspaces().Single().WorkspaceId,
                "The fixture must genuinely change ambient state before the final retry.");
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task LegacyRequestState_IsIgnoredAndSingleAmbientWorkspaceRemainsAuthoritative()
    {
        var encodedWorkspaceId = Guid.NewGuid().ToString("N");
        var ambientWorkspaceId = Guid.NewGuid().ToString("N");
        var manager = new ConfigurableWorkspaceManager(WorkspaceStatus(ambientWorkspaceId));
        SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(
            failureDetail: null,
            expectedWorkspaceId: ambientWorkspaceId);
        try
        {
            await using var harness = await CreateHarnessAsync(
                protocolVersion: "2025-11-25",
                elicitationHandler: (_, _) => ValueTask.FromResult(AcceptedPathResult()),
                manager);
            var encodedState = RequestStateCodec.CaptureWorkspaceId(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
                        JsonSerializer.SerializeToElement(encodedWorkspaceId),
                },
                ElicitationAllowlistPolicy.WorkspaceIdParameterName);

            var response = await harness.Client.SendRequestAsync(
                new JsonRpcRequest
                {
                    Method = RequestMethods.ToolsCall,
                    Params = new JsonObject
                    {
                        ["name"] = WorkspaceStatusToolName,
                        ["arguments"] = new JsonObject(),
                        ["requestState"] = encodedState,
                    },
                },
                CancellationToken.None);

            var result = JsonSerializer.SerializeToElement(response.Result);
            var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            StringAssert.Contains(text, ambientWorkspaceId);
            Assert.IsFalse(text.Contains(encodedWorkspaceId, StringComparison.Ordinal),
                "Legacy requests must ignore the modern requestState field.");
        }
        finally
        {
            SyntheticWorkspaceLoadTools.ResetWorkspaceStatusProbe(failureDetail: null);
        }
    }

    // ── (4) cancellation through the adapter's legacy leg ────────────────────

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task LegacySession_CancelledDuringElicitation_SurfacesProtocolCancellation_NotToolError()
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingElicitation =
            new TaskCompletionSource<ElicitResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var harness = await CreateHarnessAsync(
            protocolVersion: "2025-11-25",
            elicitationHandler: (_, _) =>
            {
                requestReceived.TrySetResult();
                return new ValueTask<ElicitResult>(pendingElicitation.Task);
            });

        try
        {
            var prior = harness.RawServerMessages.Count;
            using var cts = new CancellationTokenSource();
            var call = harness.Client.CallToolAsync(ToolName, cancellationToken: cts.Token);

            // Only cancel once the elicitation genuinely reached the client, so the adapter's
            // legacy ElicitAsync await is the thing observing cancellation (non-vacuous; same
            // discipline as SymbolDisambiguationElicitationTests).
            await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cts.CancelAsync();

            OperationCanceledException? caught = null;
            try
            {
                var result = await call;
                Assert.Fail(
                    "Expected protocol cancellation, but the call completed with " +
                    $"isError={result.IsError}. A cancelled elicitation must never be " +
                    "converted into a tool result.");
            }
            catch (OperationCanceledException ex)
            {
                // The SDK surfaces TaskCanceledException; catch the base class manually.
                caught = ex;
            }

            Assert.IsNotNull(caught);

            // Release the (now moot) elicitation and prove the server survived the cancelled
            // round trip; this also gives a deterministic wire boundary for the assertion below.
            pendingElicitation.TrySetResult(AcceptedPathResult());
            var followUp = await harness.Client.CallToolAsync(
                ToolName,
                cancellationToken: CancellationToken.None);
            Assert.IsFalse(followUp.IsError is true,
                "The server must stay healthy after a cancelled elicitation round trip.");

            foreach (var result in FindNewResults(harness.RawServerMessages, prior))
            {
                Assert.IsFalse(
                    result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
                    "Cancellation must reach the SDK as OperationCanceledException — never be " +
                    $"downgraded to a tool-error envelope. Saw: {result.GetRawText()}");
            }
        }
        finally
        {
            // Never leave the client's elicitation handler pending — an unanswered handler
            // wedges McpClient.DisposeAsync forever (see InMemoryMcpClientServerHarness remarks).
            pendingElicitation.TrySetResult(new ElicitResult { Action = "cancel" });
        }
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static ElicitResult AcceptedPathResult(string path = ElicitedPath) => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement(path),
        },
    };

    private static ElicitResult AcceptedChoice(string choice) => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement>
        {
            ["choice"] = JsonSerializer.SerializeToElement(choice),
        },
    };

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        string? protocolVersion,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> elicitationHandler,
        IWorkspaceManager? workspaceManager = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(workspaceManager ?? new ConfigurableWorkspaceManager());
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "mrtr-wire-contract-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticWorkspaceLoadTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"mrtr-wire-{protocolVersion ?? "modern"}",
            clientCapabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
            clientHandlers: new McpClientHandlers { ElicitationHandler = elicitationHandler },
            disposalFailureContext: "mrtr-wire-contract",
            cancellationToken: CancellationToken.None,
            protocolVersion: protocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static IReadOnlyList<JsonElement> FindNewResults(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount) =>
        rawMessages
            .Skip(priorMessageCount)
            .Select(static rawMessage => JsonNode.Parse(rawMessage))
            .OfType<JsonObject>()
            .Where(static message => message["result"] is not null)
            .Select(static message => JsonSerializer.SerializeToElement(message["result"]))
            .ToArray();

    private static bool AnyServerRequest(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount,
        string method) =>
        rawMessages
            .Skip(priorMessageCount)
            .Select(static rawMessage => JsonNode.Parse(rawMessage))
            .OfType<JsonObject>()
            .Any(message => (string?)message["method"] == method);

    /// <summary>
    /// The redaction contract shared with <c>tool-error-envelope-sensitive-detail-disclosure</c>:
    /// the terminal envelope stays the classified <c>InvalidArgument</c> schema-hint shape with
    /// no exception type names or stack frames.
    /// </summary>
    private static void AssertSanitizedInvalidArgumentEnvelope(JsonElement result)
    {
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.IsNotNull(text);
        StringAssert.Contains(text, "InvalidArgument");
        Assert.IsFalse(text.Contains("InputRequiredException", StringComparison.Ordinal),
            "The MRTR protocol signal must never leak into a client-facing envelope.");
        Assert.IsFalse(text.Contains("   at ", StringComparison.Ordinal),
            "Stack frames must never leak into a client-facing envelope.");
    }

    [McpServerToolType]
    private sealed class SyntheticWorkspaceLoadTools
    {
        private static int s_workspaceStatusDispatchCount;
        private static int s_workspaceLoadDispatchCount;
        private static string? s_workspaceStatusFailureDetail;
        private static string s_expectedWorkspaceId = SyntheticWorkspaceId;

        public static int WorkspaceStatusDispatchCount =>
            Volatile.Read(ref s_workspaceStatusDispatchCount);
        public static int WorkspaceLoadDispatchCount =>
            Volatile.Read(ref s_workspaceLoadDispatchCount);

        public static void ResetWorkspaceStatusProbe(
            string? failureDetail,
            string expectedWorkspaceId = SyntheticWorkspaceId)
        {
            Volatile.Write(ref s_workspaceStatusFailureDetail, failureDetail);
            Volatile.Write(ref s_expectedWorkspaceId, expectedWorkspaceId);
            Interlocked.Exchange(ref s_workspaceStatusDispatchCount, 0);
            Interlocked.Exchange(ref s_workspaceLoadDispatchCount, 0);
        }

        [McpServerTool(Name = ToolName)]
        public static string Load(string path)
        {
            Interlocked.Increment(ref s_workspaceLoadDispatchCount);
            if (!string.Equals(path, ElicitedPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("The requested path is outside the sanctioned roots.");
            }

            return JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workspaceId"] = SyntheticWorkspaceId,
                ["loadedPath"] = path,
            });
        }

        [McpServerTool(Name = WorkspaceStatusToolName)]
        public static async Task<string> Status(
            RequestContext<CallToolRequestParams> requestContext,
            string workspaceId,
            string? filePath = null,
            bool requestChoice = false,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(Volatile.Read(ref s_expectedWorkspaceId), workspaceId,
                "The filter must preserve the request's intended workspace identity.");
            Interlocked.Increment(ref s_workspaceStatusDispatchCount);
            var failureDetail = Volatile.Read(ref s_workspaceStatusFailureDetail);
            if (failureDetail is not null)
            {
                throw new InvalidOperationException(failureDetail);
            }

            string? choice = null;
            if (requestChoice)
            {
                choice = await ElicitationChoicePrompt.TryElicitChoiceAsync(
                    requestContext,
                    "choice",
                    "Pick a probe",
                    "Choose the probe result.",
                    [("first", "First"), ("second", "Second")],
                    cancellationToken);
            }

            return JsonSerializer.Serialize(new { workspaceId, filePath, choice, state = "ready" });
        }
    }

    private sealed class ConfigurableWorkspaceManager(params WorkspaceStatusDto[] workspaces) : IWorkspaceManager
    {
        private WorkspaceStatusDto[] _workspaces = workspaces;

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Volatile.Read(ref _workspaces);
        public void ReplaceWith(params WorkspaceStatusDto[] replacement) =>
            Volatile.Write(ref _workspaces, replacement);
        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId,
            string? projectName,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(
            string workspaceId,
            string filePath,
            CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) =>
            throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) =>
            throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }

    private static WorkspaceStatusDto WorkspaceStatus(string workspaceId) => new(
        WorkspaceId: workspaceId,
        LoadedPath: "C:/synthetic/loaded.slnx",
        WorkspaceVersion: 1,
        SnapshotToken: workspaceId + ":1",
        LoadedAtUtc: DateTimeOffset.UtcNow,
        ProjectCount: 1,
        DocumentCount: 1,
        Projects: Array.Empty<ProjectStatusDto>(),
        IsLoaded: true,
        IsStale: false,
        WorkspaceDiagnostics: Array.Empty<DiagnosticDto>());
}
