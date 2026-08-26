using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// preview-token-route-binding-fileops-fixall: coverage for the four apply routes bound in this
/// change — <c>create_file_apply</c>, <c>delete_file_apply</c>, <c>move_file_apply</c> and
/// <c>fix_all_apply</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, because a binding is only correct if BOTH ends agree:
/// </para>
/// <list type="bullet">
///   <item><b>Consumer half</b> (unit, no workspace) — each bound route refuses a foreign concrete
///     producer, accepts its own, and stays permissive for an untagged
///     (<see cref="PreviewKind.Unspecified"/>) token. Applies the provenance-case shape of
///     <c>ToolDispatchTests</c> to every one of the four routes rather than the single
///     representative pair that file pins.</item>
///   <item><b>Producer half</b> (integration, real isolated workspace) — the paired
///     <c>*_preview</c> service call actually records the kind the route expects. This is the half
///     that catches the highest-risk defect in this change: <c>FileOperationService</c> mints three
///     DIFFERENT kinds from three byte-identical <c>Store</c> call sites, so a copy-paste that tags
///     the delete site with <see cref="PreviewKind.FileCreate"/> compiles cleanly and would only
///     ever surface as a wrong error message.</item>
/// </list>
/// </remarks>
[TestClass]
public sealed class PreviewRouteBindingFileOpsTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ------------------------------------------------------------------
    // Consumer half — route enforcement.
    // ------------------------------------------------------------------

    /// <summary>
    /// A token minted by an unrelated producer family is refused BEFORE the service call runs, and
    /// the message names the token's actual producer, the route it should have been redeemed
    /// through, and the route that was wrongly invoked.
    /// </summary>
    [TestMethod]
    [DataRow(PreviewKind.FileCreate, "create_file_apply")]
    [DataRow(PreviewKind.FileDelete, "delete_file_apply")]
    [DataRow(PreviewKind.FileMove, "move_file_apply")]
    [DataRow(PreviewKind.FixAll, "fix_all_apply")]
    public async Task BoundRoute_ForeignProducer_RefusesBeforeServiceCall(PreviewKind routeKind, string expectedRoute)
    {
        var gate = new FakeGate();
        var serviceCallRan = false;
        var store = new FakePreviewStore(token: "tok-foreign", workspaceId: "ws-f")
        {
            Kind = PreviewKind.SymbolRename,
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ToolDispatch.ApplyByTokenAsync<FakeDto>(
                gate,
                store,
                previewToken: "tok-foreign",
                serviceCall: _ =>
                {
                    serviceCallRan = true;
                    return Task.FromResult(new FakeDto("must-not-run"));
                },
                ct: CancellationToken.None,
                expectedKind: routeKind));

        Assert.IsFalse(serviceCallRan, "The provenance guard must refuse the apply BEFORE the service call runs.");
        StringAssert.Contains(ex.Message, "rename_preview", "message must name the token's ACTUAL producer");
        StringAssert.Contains(ex.Message, "rename_apply", "message must name where the token SHOULD be redeemed");
        StringAssert.Contains(ex.Message, expectedRoute, "message must name the route that was wrongly invoked");
    }

    /// <summary>A token minted by the route's own producer family redeems unchanged.</summary>
    [TestMethod]
    [DataRow(PreviewKind.FileCreate)]
    [DataRow(PreviewKind.FileDelete)]
    [DataRow(PreviewKind.FileMove)]
    [DataRow(PreviewKind.FixAll)]
    public async Task BoundRoute_MatchingProducer_Applies(PreviewKind routeKind)
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "tok-match", workspaceId: "ws-m") { Kind = routeKind };

        var result = await ToolDispatch.ApplyByTokenAsync<FakeDto>(
            gate,
            store,
            previewToken: "tok-match",
            serviceCall: _ => Task.FromResult(new FakeDto("applied")),
            ct: CancellationToken.None,
            expectedKind: routeKind);

        StringAssert.Contains(result, "applied");
    }

    /// <summary>
    /// Binding a route must NOT break untagged producers or out-of-tree stores: a token whose
    /// recorded kind is <see cref="PreviewKind.Unspecified"/> ("no provenance claim", the
    /// <c>IPreviewStore.PeekKind</c> default) still redeems on every bound route.
    /// </summary>
    [TestMethod]
    [DataRow(PreviewKind.FileCreate)]
    [DataRow(PreviewKind.FileDelete)]
    [DataRow(PreviewKind.FileMove)]
    [DataRow(PreviewKind.FixAll)]
    public async Task BoundRoute_UnspecifiedProducer_StaysPermissive(PreviewKind routeKind)
    {
        var gate = new FakeGate();
        // Kind left at its default — the interface-default / untagged-producer shape.
        var store = new FakePreviewStore(token: "tok-untagged", workspaceId: "ws-ut");

        var result = await ToolDispatch.ApplyByTokenAsync<FakeDto>(
            gate,
            store,
            previewToken: "tok-untagged",
            serviceCall: _ => Task.FromResult(new FakeDto("applied")),
            ct: CancellationToken.None,
            expectedKind: routeKind);

        StringAssert.Contains(result, "applied",
            "An Unspecified producer kind means 'no provenance claim' and must stay permissive.");
    }

    /// <summary>
    /// Cross-family case WITHIN the file-operation trio: a create-file token redeemed through
    /// <c>move_file_apply</c> is refused and the message names the create family. This catches a
    /// mis-tagged mint site inside <c>FileOperationService</c> from the consumer side;
    /// <see cref="FilePreviews_RecordTheirOwnProducerKind"/> catches it from the producer side.
    /// </summary>
    [TestMethod]
    public async Task CreateToken_RedeemedViaMoveRoute_IsRefused()
    {
        var gate = new FakeGate();
        var store = new FakePreviewStore(token: "tok-cross", workspaceId: "ws-x")
        {
            Kind = PreviewKind.FileCreate,
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ToolDispatch.ApplyByTokenAsync<FakeDto>(
                gate,
                store,
                previewToken: "tok-cross",
                serviceCall: _ => Task.FromResult(new FakeDto("must-not-run")),
                ct: CancellationToken.None,
                expectedKind: PreviewKind.FileMove));

        StringAssert.Contains(ex.Message, "create_file_preview");
        StringAssert.Contains(ex.Message, "create_file_apply");
        StringAssert.Contains(ex.Message, "move_file_apply");
    }

    // ------------------------------------------------------------------
    // Producer half — mint-site tagging against a real workspace.
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>FileOperationService</c>'s three <c>Store</c> call sites are byte-identical apart from the
    /// kind argument, so this round-trip — mint through each real <c>*_preview</c> service call,
    /// then read the recorded kind back off the shared store — is the only assertion that can catch
    /// a copy-paste mis-tag. All three previews run against ONE isolated workspace and are asserted
    /// together so a swapped pair cannot hide behind a per-test-method assertion.
    /// </summary>
    [TestMethod]
    public async Task FilePreviews_RecordTheirOwnProducerKind()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var createPreview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                workspace.GetPath("SampleLib", "Generated", "ProvenanceCreated.cs"),
                "namespace SampleLib.Generated;\n\npublic sealed class ProvenanceCreated\n{\n}\n"),
            CancellationToken.None);

        var deletePreview = await FileOperationService.PreviewDeleteFileAsync(
            workspace.WorkspaceId,
            new DeleteFileDto(workspace.GetPath("SampleLib", "Cat.cs")),
            CancellationToken.None);

        var movePreview = await FileOperationService.PreviewMoveFileAsync(
            workspace.WorkspaceId,
            new MoveFileDto(
                workspace.GetPath("SampleLib", "Dog.cs"),
                workspace.GetPath("SampleLib", "Moved", "Dog.cs"),
                DestinationProjectName: null,
                UpdateNamespace: false),
            CancellationToken.None);

        Assert.AreEqual(
            PreviewKind.FileCreate,
            PreviewStore.PeekKind(createPreview.PreviewToken),
            "create_file_preview must mint a FileCreate token.");
        Assert.AreEqual(
            PreviewKind.FileDelete,
            PreviewStore.PeekKind(deletePreview.PreviewToken),
            "delete_file_preview must mint a FileDelete token.");
        Assert.AreEqual(
            PreviewKind.FileMove,
            PreviewStore.PeekKind(movePreview.PreviewToken),
            "move_file_preview must mint a FileMove token.");
    }

    /// <summary>
    /// The fourth producer round-trip, and the one the other three cannot stand in for:
    /// <c>FixAllService</c> is the single <c>Store</c> overload-switch in this change, so a
    /// wrong-overload edit there compiles cleanly and silently records
    /// <see cref="PreviewKind.Unspecified"/> instead of <see cref="PreviewKind.FixAll"/> — the
    /// exact defect the producer half exists to catch. Split from
    /// <c>FilePreviews_RecordTheirOwnProducerKind</c> because it needs a diagnostic id with a
    /// registered FixAll provider rather than a file-operation fixture.
    /// </summary>
    /// <remarks>
    /// IDE0161 (ConvertNamespaceCodeFixProvider) loads reliably from Features, but whether it has
    /// matches in the sample fixture drifts over time (see
    /// <c>FixAllServiceGuidanceTests.PreviewFixAll_WithActiveProvider_DoesNotReturnEmptyGuidanceFalsely</c>).
    /// When it produces no token this test reports <c>Inconclusive</c> rather than passing
    /// vacuously — a silent no-op here would restore exactly the coverage gap it was added to close.
    /// </remarks>
    [TestMethod]
    public async Task FixAllPreview_RecordsItsOwnProducerKind()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var fixAllPreview = await FixAllService.PreviewFixAllAsync(
            workspace.WorkspaceId,
            diagnosticId: "IDE0161",
            scope: "solution",
            filePath: null,
            projectName: null,
            CancellationToken.None).ConfigureAwait(false);

        if (string.IsNullOrEmpty(fixAllPreview.PreviewToken))
        {
            Assert.Inconclusive(
                "IDE0161 produced no fixable occurrences in the sample fixture, so the FixAll "
                + "producer tagging could not be exercised. Guidance: "
                + (fixAllPreview.GuidanceMessage ?? "(none)"));
        }

        Assert.AreEqual(
            PreviewKind.FixAll,
            PreviewStore.PeekKind(fixAllPreview.PreviewToken),
            "fix_all_preview must mint a FixAll token.");
    }

    /// <summary>
    /// The move preview's warning-collecting path (<c>updateNamespace: true</c>) shares the same
    /// <c>Store</c> call as the plain move, so tagging must not depend on whether warnings were
    /// collected.
    /// </summary>
    [TestMethod]
    public async Task MovePreview_WithNamespaceUpdate_StillRecordsFileMove()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await FileOperationService.PreviewMoveFileAsync(
            workspace.WorkspaceId,
            new MoveFileDto(
                workspace.GetPath("SampleLib", "Dog.cs"),
                workspace.GetPath("SampleLib", "Relocated", "Dog.cs"),
                DestinationProjectName: null,
                UpdateNamespace: true),
            CancellationToken.None);

        Assert.AreEqual(PreviewKind.FileMove, PreviewStore.PeekKind(preview.PreviewToken));
    }

    // ------------------------------------------------------------------
    // Fakes — private nested, mirroring ToolDispatchTests' own shapes. Kept local rather than
    // widened out of that file so neither test file constrains the other's fake.
    // ------------------------------------------------------------------

    private sealed record FakeDto(string Status);

    private sealed class FakeGate : IWorkspaceExecutionGate
    {
        public async Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => await action(ct).ConfigureAwait(false);

        public async Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
        {
            _ = applyStalenessPolicy;
            return await action(ct).ConfigureAwait(false);
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException("ToolDispatch must not route through RunLoadGateAsync.");

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException("ToolDispatch must not call RemoveGate.");
    }

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
        /// Producer family the fake reports. Defaults to <see cref="PreviewKind.Unspecified"/>,
        /// matching both the interface default and an untagged producer.
        /// </summary>
        public PreviewKind Kind { get; init; } = PreviewKind.Unspecified;

        public string? PeekWorkspaceId(string token) => token == _token ? _workspaceId : null;

        // null = "write set unknown", so ToolDispatch skips boundary revalidation and these
        // assertions isolate the provenance guard.
        public IReadOnlyList<string>? PeekChangedPaths(string token) => null;

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
