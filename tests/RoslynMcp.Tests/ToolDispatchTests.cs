using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

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
    /// <summary>
    /// preview-token-apply-route-provenance: a token whose recorded producer is concrete and
    /// different from the route's declared producer must be refused BEFORE the service call runs,
    /// with a message naming both the actual producer and the route it should be redeemed through.
    /// Mirrors <see cref="ApplyByTokenAsync_ChangedPathOutsideBoundary_RefusesBeforeServiceCall"/>:
    /// the guard sits inside the write gate but ahead of every mutation.
    /// </summary>
    [TestMethod]
    public async Task ApplyByTokenAsync_IncompatibleProducer_RefusesBeforeServiceCall()
    {
        var gate = new FakeGate();
        var serviceCallRan = false;
        var store = new FakePreviewStore(token: "tok-xroute", workspaceId: "ws-xr")
        {
            Kind = PreviewKind.CodeFix,
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                previewToken: "tok-xroute",
                serviceCall: _ =>
                {
                    serviceCallRan = true;
                    return Task.FromResult(new FakeResultDto("must-not-run", 0));
                },
                ct: CancellationToken.None,
                expectedKind: PreviewKind.SymbolRename));

        Assert.IsFalse(serviceCallRan, "Provenance guard must refuse the apply BEFORE the service call runs.");
        StringAssert.Contains(ex.Message, "code_fix_preview",
            "message must name the token's ACTUAL producer so the caller knows what they hold");
        StringAssert.Contains(ex.Message, "code_fix_apply",
            "message must name the apply route the token SHOULD be redeemed through");
        StringAssert.Contains(ex.Message, "rename_apply",
            "message must name the route that was wrongly invoked");
    }

    /// <summary>
    /// preview-token-apply-route-provenance: the happy path — a token whose producer matches the
    /// route's declared family redeems unchanged. Pins that binding the five named routes did not
    /// regress their own previews.
    /// </summary>
    [TestMethod]
    public async Task ApplyByTokenAsync_MatchingProducer_Applies()
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "tok-match", workspaceId: "ws-m")
        {
            Kind = PreviewKind.SymbolRename,
        };

        var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
            gate,
            store,
            previewToken: "tok-match",
            serviceCall: _ => Task.FromResult(new FakeResultDto("applied", 1)),
            ct: CancellationToken.None,
            expectedKind: PreviewKind.SymbolRename);

        StringAssert.Contains(result, "applied");
    }

    /// <summary>
    /// preview-token-apply-route-provenance: the guard fails OPEN on unknown provenance — an
    /// untagged producer or an out-of-tree store that returns the interface default
    /// <see cref="PreviewKind.Unspecified"/> still redeems on a bound route. Mirrors the
    /// null-<c>PeekChangedPaths</c> skip in
    /// <see cref="ApplyByTokenAsync_NullChangedPathsPeek_SkipsRevalidation_AndApplies"/>.
    /// </summary>
    [TestMethod]
    public async Task ApplyByTokenAsync_UnspecifiedProducer_SkipsGuard_AndApplies()
    {
        var gate = new FakeGate();
        // Kind left at its default (Unspecified) — the interface-default / legacy-store shape.
        var store = new FakePreviewStore(token: "tok-untagged", workspaceId: "ws-ut");

        var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
            gate,
            store,
            previewToken: "tok-untagged",
            serviceCall: _ => Task.FromResult(new FakeResultDto("applied", 2)),
            ct: CancellationToken.None,
            expectedKind: PreviewKind.SymbolRename);

        StringAssert.Contains(result, "applied",
            "An Unspecified producer kind means 'no provenance claim' and must stay permissive.");
    }

    /// <summary>
    /// preview-token-apply-route-provenance: an UNBOUND route (the ~10 apply families that have
    /// not declared a producer set) performs no enforcement — a concrete, unrelated producer kind
    /// still redeems. Pins the deliberate residue so it is a visible contract, not an accident.
    /// </summary>
    [TestMethod]
    public async Task ApplyByTokenAsync_UnboundRoute_DoesNotEnforceProvenance()
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "tok-unbound", workspaceId: "ws-ub")
        {
            Kind = PreviewKind.FormatRange,
        };

        var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
            gate,
            store,
            previewToken: "tok-unbound",
            serviceCall: _ => Task.FromResult(new FakeResultDto("applied", 3)),
            ct: CancellationToken.None);

        StringAssert.Contains(result, "applied",
            "A route that declares no expectedKind must keep its pre-binding behavior.");
    }

    /// <summary>
    /// preview-token-route-map-centralization: exhaustiveness pin. Every concrete
    /// <see cref="PreviewKind"/> member must resolve to BOTH a <c>*_preview</c> tool name (via
    /// <c>ToolDispatch._previewToolsByKind</c>) and a named <c>*_apply</c> route (via
    /// <c>ServerSurfaceCatalog.PreviewApplyRoutes</c>). Adding a member without its two companion
    /// entries fails HERE, at build time, instead of silently mis-directing a caller at redemption
    /// time. Enumerated rather than hand-listed so the pin cannot go stale.
    /// </summary>
    [TestMethod]
    public void PreviewKindRouteMap_EveryConcreteKind_ResolvesPreviewToolAndApplyRoute()
    {
        foreach (var kind in Enum.GetValues<PreviewKind>())
        {
            if (kind == PreviewKind.Unspecified)
            {
                // "No provenance claim" — permissive by the enum's own contract, deliberately unmapped.
                continue;
            }

            // preview-token-route-binding-editing-substrate: assert catalog MEMBERSHIP, not a
            // `*_preview` / `*_apply` suffix convention. The live surface breaks that convention
            // (`preview_code_action` / `apply_code_action`, `preview_multi_file_edit`), so a suffix
            // check would reject correct mappings while still accepting an invented tool name.
            var previewTool = ToolDispatch.PreviewToolFor(kind);
            Assert.IsTrue(
                ServerSurfaceCatalog.TryGetTool(previewTool, out var previewEntry),
                $"PreviewKind.{kind} must map to a tool that actually exists in the server surface, " +
                $"not a catch-all phrase; '{previewTool}' is not a catalog entry.");
            Assert.IsTrue(previewEntry!.ReadOnly,
                $"PreviewKind.{kind} must map to the token-ISSUING preview tool, not a mutating route.");

            var applyRoute = ToolDispatch.ApplyRouteFor(kind);
            Assert.IsTrue(
                ServerSurfaceCatalog.TryGetTool(applyRoute, out var applyEntry),
                $"PreviewKind.{kind} must resolve to a tool that actually exists in the server " +
                $"surface; '{applyRoute}' is not a catalog entry.");
            Assert.IsFalse(applyEntry!.ReadOnly,
                $"PreviewKind.{kind} must resolve to a mutating apply route.");
            Assert.AreNotEqual("apply_with_verify", applyRoute,
                $"PreviewKind.{kind} must resolve to its OWN named apply route, not the generic fallback.");
        }
    }

    /// <summary>
    /// preview-token-route-binding-editing-substrate: content pin for the centralized map. The
    /// exhaustiveness test above proves every concrete kind resolves to SOMETHING real; this one
    /// proves it resolves to the RIGHT thing, so a copy-paste slip between two neighbouring
    /// producer families fails a test instead of shipping a misleading mismatch message.
    /// Hand-listed on purpose — a table derived from the map under test would assert nothing.
    /// </summary>
    [TestMethod]
    public void PreviewKindRouteMap_EachKind_ResolvesItsOwnProducerPair()
    {
        (PreviewKind Kind, string PreviewTool, string ApplyRoute)[] expected =
        [
            (PreviewKind.SymbolRename, "rename_preview", "rename_apply"),
            (PreviewKind.FormatDocument, "format_document_preview", "format_document_apply"),
            (PreviewKind.FormatRange, "format_range_preview", "format_range_apply"),
            (PreviewKind.OrganizeUsings, "organize_usings_preview", "organize_usings_apply"),
            (PreviewKind.CodeFix, "code_fix_preview", "code_fix_apply"),
            (PreviewKind.MultiFileEdit, "preview_multi_file_edit", "preview_multi_file_edit_apply"),
            (PreviewKind.FileCreate, "create_file_preview", "create_file_apply"),
            (PreviewKind.FileDelete, "delete_file_preview", "delete_file_apply"),
            (PreviewKind.FileMove, "move_file_preview", "move_file_apply"),
            (PreviewKind.CodeAction, "preview_code_action", "apply_code_action"),
            (PreviewKind.FixAll, "fix_all_preview", "fix_all_apply"),
            (PreviewKind.ExtractMethod, "extract_method_preview", "extract_method_apply"),
            (PreviewKind.ExtractInterface, "extract_interface_preview", "extract_interface_apply"),
            (PreviewKind.ExtractType, "extract_type_preview", "extract_type_apply"),
            (PreviewKind.MoveTypeToFile, "move_type_to_file_preview", "move_type_to_file_apply"),
        ];

        foreach (var (kind, previewTool, applyRoute) in expected)
        {
            Assert.AreEqual(previewTool, ToolDispatch.PreviewToolFor(kind),
                $"PreviewKind.{kind} is mapped to the wrong *_preview producer.");
            Assert.AreEqual(applyRoute, ToolDispatch.ApplyRouteFor(kind),
                $"PreviewKind.{kind} resolves to the wrong apply route.");
        }

        var concreteKinds = Enum.GetValues<PreviewKind>()
            .Where(static kind => kind != PreviewKind.Unspecified)
            .ToArray();
        CollectionAssert.AreEquivalent(
            concreteKinds,
            expected.Select(static row => row.Kind).ToArray(),
            "A new concrete PreviewKind member must be added to this content pin too — otherwise a " +
            "typo'd map entry ships silently once the exhaustiveness test is satisfied.");
    }

    /// <summary>
    /// preview-token-route-map-centralization: the fail-loud arm. A concrete-but-unmapped kind
    /// (simulated with an undefined enum value, so no scratch member has to be added) must throw
    /// and name both map sites, replacing the old silent
    /// <c>"an untagged *_preview tool"</c> / <c>"apply_with_verify"</c> catch-alls that told the
    /// caller something factually wrong.
    /// </summary>
    [TestMethod]
    public void PreviewKindRouteMap_UnmappedConcreteKind_ThrowsInsteadOfReportingACatchAll()
    {
        const PreviewKind Unmapped = (PreviewKind)999;

        var previewEx = Assert.ThrowsExactly<InvalidOperationException>(
            () => ToolDispatch.PreviewToolFor(Unmapped));
        StringAssert.Contains(previewEx.Message, "999",
            "the failure must name the unmapped member so the fix is mechanical");
        StringAssert.Contains(previewEx.Message, "_previewToolsByKind",
            "the failure must point at the kind → *_preview map site");
        StringAssert.Contains(previewEx.Message, "PreviewApplyRoutes",
            "the failure must point at the catalog map site too — both need the new entry");

        var applyEx = Assert.ThrowsExactly<InvalidOperationException>(
            () => ToolDispatch.ApplyRouteFor(Unmapped));
        StringAssert.Contains(applyEx.Message, "999",
            "the apply-route half must fail loud on the same unmapped member");
        Assert.IsFalse(applyEx.Message.Contains("apply_with_verify", StringComparison.Ordinal),
            "the generic route must no longer be offered as the answer for an unmapped kind");
    }

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
    public async Task ApplyByTokenAsync_UnknownToken_ThrowsPreviewTokenStaleException_WithTokenAndRecoveryHint()
    {
        // preview-token-stale-across-auto-reload: when PeekWorkspaceId returns null (token
        // never stored, TTL-expired, or invalidated by InvalidateOnVersionBump after an
        // auto-reload), ApplyByTokenAsync must throw PreviewTokenStaleException rather than
        // a bare KeyNotFoundException so ToolErrorHandler can emit category=PreviewTokenStale
        // with a re-issue-preview recovery hint instead of the generic NotFound envelope.
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "real-token", workspaceId: "ws-x");

        var ex = await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() =>
            ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                previewToken: "bogus-token",
                serviceCall: _ => Task.FromResult(new FakeResultDto("unused", 0)),
                ct: CancellationToken.None));

        Assert.AreEqual("bogus-token", ex.PreviewToken,
            "PreviewToken property must carry the rejected token for structured envelope use");
        StringAssert.Contains(ex.Message, "bogus-token",
            "exception message must name the failing token so log scrapers can correlate");
        StringAssert.Contains(ex.Message, "*_preview",
            "exception message must direct the caller to the recovery action (re-issue paired *_preview)");
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
    /// preview-apply-token-write-path-toctou: a store that cannot enumerate the preview's write
    /// set (<c>PeekChangedPaths</c> returns <see langword="null"/> — the interface default) must
    /// pass through even when a restrictive boundary snapshot is set: <see langword="null"/>
    /// means "unknown", and the delegate-peek stores that hit this shape are covered by their
    /// own spin-off row, not silently blocked here.
    /// </summary>
    [TestMethod]
    [DoNotParallelize] // mutates the process-global SecurityOptionsSnapshot
    public async Task ApplyByTokenAsync_NullChangedPathsPeek_SkipsRevalidation_AndApplies()
    {
        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions
            {
                SanctionedRoots = [Path.Combine(TestTempRoot.Current, "nonexistent-boundary")],
            };
            var gate = new FakeGate();
            var store = new FakePreviewStore(token: "tok-null-peek", workspaceId: "ws-np");

            var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                previewToken: "tok-null-peek",
                serviceCall: _ => Task.FromResult(new FakeResultDto("applied", 1)),
                ct: CancellationToken.None);

            StringAssert.Contains(result, "applied",
                "A null PeekChangedPaths (write set unknown) must skip revalidation, not refuse the apply.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
        }
    }

    /// <summary>
    /// preview-apply-token-write-path-toctou: a <see langword="null"/>
    /// <see cref="SecurityOptionsSnapshot"/> means "unbooted host" (the unit-test path — the
    /// production host always populates it at startup, <c>Program.cs</c>). Per the snapshot's own
    /// documented contract that state is "unknown", NOT "unconfigured", so revalidation is
    /// skipped rather than fabricating a fail-closed boundary. This test asserts and documents
    /// the skip so it never silently becomes the production path.
    /// </summary>
    [TestMethod]
    [DoNotParallelize] // mutates the process-global SecurityOptionsSnapshot
    public async Task ApplyByTokenAsync_NullSecuritySnapshot_SkipsRevalidation_AndApplies()
    {
        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = null;
            var gate = new FakeGate();
            var store = new FakePreviewStore(token: "tok-null-snap", workspaceId: "ws-ns")
            {
                ChangedPaths = [Path.Combine(TestTempRoot.Current, "anywhere", "outside.cs")],
            };

            var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                previewToken: "tok-null-snap",
                serviceCall: _ => Task.FromResult(new FakeResultDto("applied", 2)),
                ct: CancellationToken.None);

            StringAssert.Contains(result, "applied",
                "A null SecurityOptionsSnapshot (unbooted host) must skip revalidation, not refuse the apply.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
        }
    }

    /// <summary>
    /// preview-apply-token-write-path-toctou: with a configured boundary AND an enumerable write
    /// set, a changed path outside the boundary refuses the redemption before the service call
    /// runs. (The full link-swap scenario lives in
    /// <c>PreviewApplyBoundaryRevalidationTests</c>; this is the pure-unit pin on the dispatch
    /// helper's own contract.)
    /// </summary>
    [TestMethod]
    [DoNotParallelize] // mutates the process-global SecurityOptionsSnapshot
    public async Task ApplyByTokenAsync_ChangedPathOutsideBoundary_RefusesBeforeServiceCall()
    {
        var boundary = Path.Combine(TestTempRoot.Current, "tdt-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(boundary);
        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [boundary] };
            var gate = new FakeGate();
            var serviceCallRan = false;
            var store = new FakePreviewStore(token: "tok-oob", workspaceId: "ws-oob")
            {
                ChangedPaths = [Path.Combine(TestTempRoot.Current, "elsewhere", "outside.cs")],
            };

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                    gate,
                    store,
                    previewToken: "tok-oob",
                    serviceCall: _ =>
                    {
                        serviceCallRan = true;
                        return Task.FromResult(new FakeResultDto("must-not-run", 0));
                    },
                    ct: CancellationToken.None));

            Assert.IsFalse(serviceCallRan, "Revalidation must refuse the apply BEFORE the service call runs.");
            Assert.AreEqual(1, gate.WriteCallCount, "Revalidation runs INSIDE the write gate.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
        }
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

        /// <summary>
        /// preview-apply-token-write-path-toctou: write set the fake reports for its token, or
        /// <see langword="null"/> (the default, matching the interface's default implementation)
        /// for "unknown".
        /// </summary>
        public IReadOnlyList<string>? ChangedPaths { get; init; }

        /// <summary>
        /// preview-token-apply-route-provenance: producer family the fake reports for its token.
        /// Defaults to <see cref="PreviewKind.Unspecified"/>, matching both the interface default
        /// and an untagged producer.
        /// </summary>
        public PreviewKind Kind { get; init; } = PreviewKind.Unspecified;

        public string? PeekWorkspaceId(string token) => token == _token ? _workspaceId : null;

        public IReadOnlyList<string>? PeekChangedPaths(string token) => token == _token ? ChangedPaths : null;

        public PreviewKind PeekKind(string token) => token == _token ? Kind : PreviewKind.Unspecified;

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

        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) => throw new NotSupportedException();
    }
}
