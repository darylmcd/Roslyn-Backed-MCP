using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit coverage for <see cref="ToolDispatch"/> — the shared runtime helper that
/// every inline-delegating MCP tool shim routes through. Verifies the three dispatch
/// kinds end-to-end without needing a live workspace: the gate and preview-store
/// dependencies are stubbed in-test so the assertions focus on the helper's own
/// behavior (gate verb selection, token-to-ws mapping, JSON serialization,
/// KeyNotFoundException shape).
/// </summary>
/// <remarks>
/// <para>
/// Per-tool shim methods (e.g. <c>CodeActionTools.ApplyCodeAction</c>) are covered
/// by the integration-test suite under <c>tests/RoslynMcp.Tests/Expanded*</c>; this
/// unit file guards only the helper's own contract so that any future adjustment to
/// the gate or serialization path trips a local test before surfacing through the
/// larger integration suites.
/// </para>
/// </remarks>
[TestClass]
public sealed class ToolDispatchTests
{
    // Distinguishable payload type so the assertions can inspect JSON structure.
    private sealed record FakeResultDto(string Message, int Count);

    [TestMethod]
    public async Task ApplyByTokenAsync_ResolvesWorkspaceId_AndReturnsSerializedResult()
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "tok-1", workspaceId: "ws-abc");
        var expected = new FakeResultDto("hello", 42);

        var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
            gate,
            store,
            previewToken: "tok-1",
            serviceCall: _ => Task.FromResult(expected),
            ct: CancellationToken.None);

        // Gate verb must be Write — apply tools mutate the workspace.
        Assert.AreEqual(1, gate.WriteCallCount, "ApplyByTokenAsync must dispatch via RunWriteAsync");
        Assert.AreEqual(0, gate.ReadCallCount, "ApplyByTokenAsync must NOT dispatch via RunReadAsync");
        Assert.AreEqual("ws-abc", gate.LastWriteWorkspaceId,
            "ApplyByTokenAsync must resolve workspaceId from the preview token via PeekWorkspaceId");

        // The JSON shape matches the hand-written shims' output: indented, camelCase, enum as string.
        using var doc = JsonDocument.Parse(result);
        Assert.AreEqual("hello", doc.RootElement.GetProperty("message").GetString());
        Assert.AreEqual(42, doc.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task ApplyByTokenAsync_UnknownToken_ThrowsKeyNotFoundException_WithExpectedMessage()
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "real-token", workspaceId: "ws-x");

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                previewToken: "bogus-token",
                serviceCall: _ => Task.FromResult(new FakeResultDto("unused", 0)),
                ct: CancellationToken.None));

        // Matches the hand-written shim format — migration must not change error envelope shape.
        Assert.AreEqual("Preview token 'bogus-token' not found or expired.", ex.Message);
        Assert.AreEqual(0, gate.WriteCallCount, "Must fail before entering the write gate");
    }

    [TestMethod]
    public async Task PreviewWithWorkspaceIdAsync_RunsUnderWriteGate_AndReturnsSerializedResult()
    {
        var gate = new FakeGate();
        var expected = new FakeResultDto("preview-out", 7);

        var result = await ToolDispatch.PreviewWithWorkspaceIdAsync<FakeResultDto>(
            gate,
            workspaceId: "ws-preview",
            serviceCall: _ => Task.FromResult(expected),
            ct: CancellationToken.None);

        Assert.AreEqual(1, gate.WriteCallCount, "PreviewWithWorkspaceIdAsync must dispatch via RunWriteAsync");
        Assert.AreEqual(0, gate.ReadCallCount, "PreviewWithWorkspaceIdAsync must NOT dispatch via RunReadAsync");
        Assert.AreEqual("ws-preview", gate.LastWriteWorkspaceId);

        using var doc = JsonDocument.Parse(result);
        Assert.AreEqual("preview-out", doc.RootElement.GetProperty("message").GetString());
        Assert.AreEqual(7, doc.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task ReadByWorkspaceIdAsync_RunsUnderReadGate_AndReturnsSerializedResult()
    {
        var gate = new FakeGate();
        var expected = new FakeResultDto("read-out", 99);

        var result = await ToolDispatch.ReadByWorkspaceIdAsync<FakeResultDto>(
            gate,
            workspaceId: "ws-read",
            serviceCall: _ => Task.FromResult(expected),
            ct: CancellationToken.None);

        // Gate verb must be Read — the key behavioral distinction from PreviewWithWorkspaceIdAsync.
        Assert.AreEqual(0, gate.WriteCallCount, "ReadByWorkspaceIdAsync must NOT dispatch via RunWriteAsync");
        Assert.AreEqual(1, gate.ReadCallCount, "ReadByWorkspaceIdAsync must dispatch via RunReadAsync");
        Assert.AreEqual("ws-read", gate.LastReadWorkspaceId);

        using var doc = JsonDocument.Parse(result);
        Assert.AreEqual("read-out", doc.RootElement.GetProperty("message").GetString());
        Assert.AreEqual(99, doc.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task ApplyByTokenAsync_PassesCancellationTokenToServiceCall()
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "tok-ct", workspaceId: "ws-ct");
        using var cts = new CancellationTokenSource();
        CancellationToken seenByService = CancellationToken.None;

        await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
            gate,
            store,
            previewToken: "tok-ct",
            serviceCall: ct =>
            {
                seenByService = ct;
                return Task.FromResult(new FakeResultDto("done", 1));
            },
            ct: cts.Token);

        // The CT flowing into the service call is the gate's nested token (the gate may wrap
        // the caller's CT with per-request timeout linking). Assert it is at minimum usable —
        // not the default token — so we know the helper is not dropping it.
        Assert.IsTrue(seenByService != default, "serviceCall must receive a non-default CancellationToken");
    }

    /// <summary>
    /// Minimal stand-in for <see cref="IWorkspaceExecutionGate"/> that records which verb
    /// was invoked and the workspaceId that was passed. <c>RunLoadGateAsync</c> and
    /// <c>RemoveGate</c> are not exercised by <see cref="ToolDispatch"/> so they throw to
    /// fail loudly if a future refactor accidentally routes through them.
    /// </summary>
    private sealed class FakeGate : IWorkspaceExecutionGate
    {
        public int ReadCallCount { get; private set; }
        public int WriteCallCount { get; private set; }
        public string? LastReadWorkspaceId { get; private set; }
        public string? LastWriteWorkspaceId { get; private set; }

        public async Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            ReadCallCount++;
            LastReadWorkspaceId = workspaceId;
            // Pass a fresh CTS-linked token so the PassesCancellationTokenToServiceCall test
            // can distinguish "helper forwarded a token" from "helper dropped to default".
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return await action(linked.Token).ConfigureAwait(false);
        }

        public async Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
        {
            _ = applyStalenessPolicy;
            WriteCallCount++;
            LastWriteWorkspaceId = workspaceId;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return await action(linked.Token).ConfigureAwait(false);
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException("ToolDispatch must not route through RunLoadGateAsync.");

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException("ToolDispatch must not call RemoveGate.");
    }

    /// <summary>
    /// Minimal stand-in for <see cref="IPreviewStore"/> exposing only
    /// <c>PeekWorkspaceId</c> behavior — the only method <see cref="ToolDispatch"/>
    /// consumes. All other members throw.
    /// </summary>
    private sealed class FakePreviewStore : IPreviewStore
    {
        private readonly string _token;
        private readonly string _workspaceId;

        public FakePreviewStore(string token, string workspaceId)
        {
            _token = token;
            _workspaceId = workspaceId;
        }

        public string? PeekWorkspaceId(string token) => token == _token ? _workspaceId : null;

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
            => throw new NotSupportedException();

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
            => throw new NotSupportedException();

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes)
            => throw new NotSupportedException();

        public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
            => throw new NotSupportedException();

        public void Invalidate(string token) => throw new NotSupportedException();

        public void InvalidateAll(string? workspaceId = null) => throw new NotSupportedException();
    }
}
