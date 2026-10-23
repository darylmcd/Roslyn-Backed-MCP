using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// preview-token-route-binding-multifile-codeaction: pins the producer binding of the two
/// editing-family apply routes — <c>preview_multi_file_edit_apply</c> and
/// <c>apply_code_action</c> — at the SHIM level.
/// </summary>
/// <remarks>
/// <para>
/// <c>ToolDispatchTests</c> already covers <see cref="ToolDispatch.RequireCompatibleProducer"/>'s
/// own fail-open/fail-closed contract. What these cases guard is the half that lives in the tool
/// shims: that <c>MultiFileEditTools.ApplyMultiFilePreview</c> and
/// <c>CodeActionTools.ApplyCodeAction</c> actually declare the RIGHT
/// <see cref="PreviewKind"/>. The required dispatch arguments make a dropped binding fail to
/// compile; these shim-level tests still catch a concrete but incorrect kind or route name.
/// </para>
/// </remarks>
[TestClass]
public sealed class PreviewRouteBindingEditingTests
{
    // ------------------------------------------------------------------
    // preview_multi_file_edit_apply  ->  PreviewKind.MultiFileEdit
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task MultiFileEditApply_IncompatibleProducer_RefusesBeforeServiceCall()
    {
        var gate = new ForwardingDispatchGate();
        var refactoring = new RecordingApplyRefactoringService("MultiFile.cs");
        var store = new DispatchPreviewStore(token: "tok-mfe-x", workspaceId: "ws-mfe-x")
        {
            Kind = PreviewKind.SymbolRename,
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            MultiFileEditTools.ApplyMultiFilePreview(gate, refactoring, store, "tok-mfe-x", CancellationToken.None));

        Assert.AreEqual(0, refactoring.ApplyCallCount,
            "The provenance guard must refuse the apply BEFORE the service call runs.");
        StringAssert.Contains(ex.Message, "rename_preview",
            "message must name the token's ACTUAL producer");
        StringAssert.Contains(ex.Message, "rename_apply",
            "message must name the apply route the token SHOULD be redeemed through");
        StringAssert.Contains(ex.Message, "preview_multi_file_edit_apply",
            "message must name the route that was wrongly invoked");
    }

    [TestMethod]
    public async Task MultiFileEditApply_MatchingProducer_Applies()
    {
        var gate = new ForwardingDispatchGate();
        var refactoring = new RecordingApplyRefactoringService("MultiFile.cs");
        var store = new DispatchPreviewStore(token: "tok-mfe-ok", workspaceId: "ws-mfe-ok")
        {
            Kind = PreviewKind.MultiFileEdit,
        };

        var result = await MultiFileEditTools.ApplyMultiFilePreview(
            gate, refactoring, store, "tok-mfe-ok", CancellationToken.None);

        Assert.AreEqual(1, refactoring.ApplyCallCount);
        StringAssert.Contains(result, "MultiFile.cs");
    }

    [TestMethod]
    public async Task MultiFileEditApply_UnspecifiedProducer_StaysPermissive()
    {
        var gate = new ForwardingDispatchGate();
        var refactoring = new RecordingApplyRefactoringService("MultiFile.cs");
        // Kind left at its default (Unspecified) — an untagged producer or an out-of-tree store.
        var store = new DispatchPreviewStore(token: "tok-mfe-untagged", workspaceId: "ws-mfe-ut");

        var result = await MultiFileEditTools.ApplyMultiFilePreview(
            gate, refactoring, store, "tok-mfe-untagged", CancellationToken.None);

        Assert.AreEqual(1, refactoring.ApplyCallCount,
            "Unspecified means 'no provenance claim' and must keep redeeming on a bound route.");
        StringAssert.Contains(result, "MultiFile.cs");
    }

    // ------------------------------------------------------------------
    // apply_code_action  ->  PreviewKind.CodeAction
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyCodeAction_IncompatibleProducer_RefusesBeforeServiceCall()
    {
        var gate = new ForwardingDispatchGate();
        var refactoring = new RecordingApplyRefactoringService("MultiFile.cs");
        var store = new DispatchPreviewStore(token: "tok-ca-x", workspaceId: "ws-ca-x")
        {
            Kind = PreviewKind.SymbolRename,
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            CodeActionTools.ApplyCodeAction(gate, refactoring, store, "tok-ca-x", CancellationToken.None));

        Assert.AreEqual(0, refactoring.ApplyCallCount,
            "The provenance guard must refuse the apply BEFORE the service call runs.");
        StringAssert.Contains(ex.Message, "rename_preview",
            "message must name the token's ACTUAL producer");
        StringAssert.Contains(ex.Message, "rename_apply",
            "message must name the apply route the token SHOULD be redeemed through");
        StringAssert.Contains(ex.Message, "apply_code_action",
            "message must name the route that was wrongly invoked");
    }

    [TestMethod]
    public async Task ApplyCodeAction_MatchingProducer_Applies()
    {
        var gate = new ForwardingDispatchGate();
        var refactoring = new RecordingApplyRefactoringService("MultiFile.cs");
        var store = new DispatchPreviewStore(token: "tok-ca-ok", workspaceId: "ws-ca-ok")
        {
            Kind = PreviewKind.CodeAction,
        };

        var result = await CodeActionTools.ApplyCodeAction(
            gate, refactoring, store, "tok-ca-ok", CancellationToken.None);

        Assert.AreEqual(1, refactoring.ApplyCallCount);
        StringAssert.Contains(result, "MultiFile.cs");
    }

    [TestMethod]
    public async Task ApplyCodeAction_UnspecifiedProducer_StaysPermissive()
    {
        var gate = new ForwardingDispatchGate();
        var refactoring = new RecordingApplyRefactoringService("MultiFile.cs");
        var store = new DispatchPreviewStore(token: "tok-ca-untagged", workspaceId: "ws-ca-ut");

        var result = await CodeActionTools.ApplyCodeAction(
            gate, refactoring, store, "tok-ca-untagged", CancellationToken.None);

        Assert.AreEqual(1, refactoring.ApplyCallCount,
            "Unspecified means 'no provenance claim' and must keep redeeming on a bound route.");
        StringAssert.Contains(result, "MultiFile.cs");
    }

}

/// <summary>
/// Producer round-trip: real services must persist their intended provenance through the
/// required IPreviewStore contract, so route binding remains effective beyond shim tests.
/// </summary>
// donotparallelize-audit-wave-20: [DoNotParallelize] removed. Both methods only read the shared
// SampleSolution through the synchronized WorkspaceIdCache and mint a preview token into the
// thread-safe shared PreviewStore — PreviewMultiFileTextEditsAsync / PreviewCodeActionAsync are
// in-memory previews with no apply, no disk write, no reload/version bump — then PeekKind and
// Invalidate only their OWN token. No direct WorkspaceManager.LoadAsync/Close, no InvalidateAll,
// no static or environment state. Verified with a repeated (3x) run of this class alongside its
// donotparallelize-audit-wave-20 siblings, green every time.
[TestClass]
public sealed class PreviewRouteBindingEditingProducerTests : SharedWorkspaceTestBase
{
    private static string SampleWorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SampleWorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(SampleWorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    [TestMethod]
    public async Task PreviewMultiFileEdit_RecordsMultiFileEditKind()
    {
        // TestBase wires the shared EditService with previewStore: null, so build one bound to
        // the shared store the assertion reads back from.
        var editService = new EditService(
            WorkspaceManager,
            NullLogger<EditService>.Instance,
            UndoService,
            ChangeTracker,
            PreviewStore,
            CompileCheckService);

        var fileEdits = new[]
        {
            new FileEditsDto(
                FindDocumentPath("Dog.cs"),
                new[] { new TextEditDto(1, 1, 1, 1, "// preview-token provenance probe\n") }),
        };

        var preview = await editService.PreviewMultiFileTextEditsAsync(
            SampleWorkspaceId, fileEdits, CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.AreEqual(PreviewKind.MultiFileEdit, PreviewStore.PeekKind(preview.PreviewToken),
            "preview_multi_file_edit must record its producer family, or preview_multi_file_edit_apply " +
            "stays permissive despite being bound.");

        PreviewStore.Invalidate(preview.PreviewToken);
    }

    [TestMethod]
    public async Task PreviewCodeAction_RecordsCodeActionKind()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        var actions = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId, filePath,
            startLine: 22, startColumn: 16, endLine: 22, endColumn: 41,
            CancellationToken.None);

        var action = actions.Actions.FirstOrDefault(a =>
            a.Title.Contains("Introduce parameter", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(action, "Expected a previewable 'Introduce parameter' leaf action.");

        var preview = await CodeActionService.PreviewCodeActionAsync(
            SampleWorkspaceId, filePath,
            startLine: 22, startColumn: 16, endLine: 22, endColumn: 41,
            action.Index,
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.AreEqual(PreviewKind.CodeAction, PreviewStore.PeekKind(preview.PreviewToken),
            "preview_code_action must record its producer family, or apply_code_action stays " +
            "permissive despite being bound.");

        PreviewStore.Invalidate(preview.PreviewToken);
    }
}
