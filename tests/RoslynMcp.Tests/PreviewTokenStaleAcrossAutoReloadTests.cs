using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>preview-token-stale-across-auto-reload</c> (gh #767, P1
/// firewallanalyzer audit): an <c>*_apply</c> tool call whose paired preview was
/// invalidated by an auto-reload version bump used to surface as a bare
/// <see cref="KeyNotFoundException"/> with <c>category="NotFound"</c>, indistinguishable
/// from "workspace not found" or "symbol not found." The remediation introduces
/// <see cref="PreviewTokenStaleException"/> at the two <c>?? throw</c> sites
/// (<c>ToolDispatch.ApplyByTokenAsync</c>, <c>ApplyWithVerifyTool.ApplyWithVerify</c>)
/// and a dedicated <c>PreviewTokenStale</c> classifier entry in
/// <see cref="ToolErrorHandler"/> registered BEFORE the generic
/// <see cref="InvalidOperationException"/> handler so the more-specific category wins
/// on the dictionary walk.
/// </summary>
/// <remarks>
/// The test surface covers three orthogonal axes:
/// <list type="bullet">
///   <item><description><b>End-to-end repro</b> — store a token in a real
///     <see cref="PreviewStore"/>, evict via the production
///     <see cref="IPreviewStore.InvalidateOnVersionBump"/> call (the same path
///     <c>WorkspaceManager.LoadIntoSessionAsync</c> invokes after every reload), then
///     dispatch through <see cref="ToolDispatch.ApplyByTokenAsync{TDto}"/> and assert
///     the envelope category and message shape.</description></item>
///   <item><description><b>Classifier-direct</b> — feed the new exception directly to
///     <see cref="ToolExecutionTestHarness"/> so the classifier wiring is asserted
///     independently of the dispatch wrapper.</description></item>
///   <item><description><b>Registration-order safety net</b> — a bare
///     <see cref="InvalidOperationException"/> must still surface as <c>InvalidOperation</c>,
///     proving the dedicated <c>PreviewTokenStale</c> entry does not capture sibling
///     subclasses by accident.</description></item>
/// </list>
/// </remarks>
[TestClass]
public sealed class PreviewTokenStaleAcrossAutoReloadTests
{
    [TestMethod]
    public async Task DispatchPath_WhenTokenInvalidatedByVersionBump_ReturnsPreviewTokenStaleCategory()
    {
        // Real PreviewStore — exercises the production InvalidateOnVersionBump path that
        // WorkspaceManager.LoadIntoSessionAsync calls after every reload. DefaultMaxVersionSpan
        // is 1, so storing at V=1 and bumping to V=3 pushes the token past its pinned ceiling
        // (V=2) and triggers the same drop as a real preview → apply → reload → apply sequence
        // where two consecutive auto-reloads happen between preview and apply.
        var store = new PreviewStore();
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        const string workspaceId = "ws-repro";
        var token = store.Store(workspaceId, solution, workspaceVersion: 1, "scaffold_test_preview");

        // Confirm the token is initially valid (guards against test setup drift).
        Assert.IsNotNull(store.PeekWorkspaceId(token),
            "test setup precondition: token must be redeemable before the version bump");

        // Simulate two consecutive auto-reloads: V=1 → V=2 (kept) → V=3 (drops because
        // pinned ceiling = StoreVersion + DefaultMaxVersionSpan = 1 + 1 = 2).
        store.InvalidateOnVersionBump(workspaceId, newWorkspaceVersion: 3);
        Assert.IsNull(store.PeekWorkspaceId(token),
            "test setup precondition: token must be invalidated by the version bump before the dispatch call");

        // Route through the same harness production code uses for tool error formatting.
        // The dispatch helper throws PreviewTokenStaleException; the harness classifies and
        // serializes the envelope exactly as StructuredCallToolFilter does on the real path.
        var envelope = await ToolExecutionTestHarness.RunAsync(
            "scaffold_test_apply",
            () => ToolDispatch.ApplyByTokenAsync<UnusedDto>(
                new ThrowingGate(),
                store,
                previewToken: token,
                serviceCall: _ => Task.FromResult(new UnusedDto()),
                ct: CancellationToken.None));

        var doc = JsonDocument.Parse(envelope);
        Assert.AreEqual("PreviewTokenStale",
            doc.RootElement.GetProperty("category").GetString(),
            $"expected PreviewTokenStale category, full payload: {envelope}");
        Assert.IsTrue(doc.RootElement.GetProperty("error").GetBoolean(),
            "error envelope must set error=true");
        Assert.AreEqual("scaffold_test_apply",
            doc.RootElement.GetProperty("tool").GetString(),
            "envelope must carry the originating tool name through the harness");

        var message = doc.RootElement.GetProperty("message").GetString()!;
        Assert.IsFalse(message.Contains(token, StringComparison.Ordinal),
            "the public envelope must not echo the rejected preview token");
        StringAssert.Contains(message, "workspace was reloaded",
            "envelope must explain the version-bump invalidation lifecycle");
        StringAssert.Contains(message, "Re-issue the paired *_preview call",
            "envelope must direct the caller to the recovery action");
    }

    [TestMethod]
    public async Task Classifier_PreviewTokenStaleException_MapsToDedicatedCategory()
    {
        // Asserts the classifier wiring independently of the dispatch helper: feed the
        // exception directly and verify the dedicated entry in ToolErrorHandler.ErrorHandlers
        // is reached (rather than the base InvalidOperationException entry registered later
        // in the dictionary).
        var envelope = await ToolExecutionTestHarness.RunAsync(
            "apply_with_verify",
            () => throw new PreviewTokenStaleException(
                "tok-abc123",
                "Preview token 'tok-abc123' has expired or was invalidated."));

        var doc = JsonDocument.Parse(envelope);
        Assert.AreEqual("PreviewTokenStale",
            doc.RootElement.GetProperty("category").GetString(),
            "registration order must make PreviewTokenStaleException win over InvalidOperationException");

        var message = doc.RootElement.GetProperty("message").GetString()!;
        Assert.IsFalse(message.Contains("tok-abc123", StringComparison.Ordinal),
            "classifier must not expose the PreviewToken property");
        StringAssert.Contains(message, "Re-issue the paired *_preview call");
    }

    [TestMethod]
    public async Task Classifier_BareInvalidOperationException_StillMapsToInvalidOperationCategory()
    {
        // Registration-order safety net: introducing PreviewTokenStaleException (which derives
        // from InvalidOperationException) must NOT capture sibling InvalidOperationException
        // throws. If the dictionary walked the entries in the wrong order (or used the base
        // type for matching), a bare InvalidOperationException would incorrectly surface as
        // PreviewTokenStale. This guards against that regression.
        var envelope = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => throw new InvalidOperationException("workspace stale"));

        var doc = JsonDocument.Parse(envelope);
        Assert.AreEqual("InvalidOperation",
            doc.RootElement.GetProperty("category").GetString(),
            "a bare InvalidOperationException must still classify as InvalidOperation; otherwise " +
            "the new PreviewTokenStale entry is over-matching");
    }

    [TestMethod]
    public async Task Classifier_KeyNotFoundException_UnaffectedByNewEntry()
    {
        // Negative test: the new PreviewTokenStaleException derives from
        // InvalidOperationException (not KeyNotFoundException), so a bare
        // KeyNotFoundException must continue to surface as the standard NotFound envelope.
        // Guards against an accidental re-base of the exception hierarchy.
        var envelope = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => throw new KeyNotFoundException("symbol not found"));

        var doc = JsonDocument.Parse(envelope);
        Assert.AreEqual("NotFound",
            doc.RootElement.GetProperty("category").GetString(),
            "KeyNotFoundException classification must be unaffected by the new PreviewTokenStale entry");
    }

    /// <summary>
    /// Trivial DTO placeholder — the dispatch path fails before <c>serviceCall</c> runs, so
    /// the payload type only needs to satisfy the generic constraint.
    /// </summary>
    private sealed record UnusedDto;

    /// <summary>
    /// Stand-in for <see cref="IWorkspaceExecutionGate"/> that throws on any call. The dispatch
    /// path must fail with <see cref="PreviewTokenStaleException"/> at the
    /// <c>PeekWorkspaceId(...) ?? throw</c> site BEFORE entering the gate; if the gate is
    /// reached, this throw makes the test fail loudly with the actual misroute.
    /// </summary>
    private sealed class ThrowingGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new InvalidOperationException(
                $"ThrowingGate.RunReadAsync invoked with workspaceId='{workspaceId}' — dispatch path must fail before reaching the gate");

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
            => throw new InvalidOperationException(
                $"ThrowingGate.RunWriteAsync invoked with workspaceId='{workspaceId}' — dispatch path must fail before reaching the gate");

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new InvalidOperationException("ThrowingGate.RunLoadGateAsync must not be reached on the stale-token path");

        public void RemoveGate(string workspaceId)
            => throw new InvalidOperationException("ThrowingGate.RemoveGate must not be reached on the stale-token path");
    }
}
