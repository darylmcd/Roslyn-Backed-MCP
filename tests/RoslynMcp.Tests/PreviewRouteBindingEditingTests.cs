using Microsoft.CodeAnalysis;
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
/// <see cref="PreviewKind"/>. Dropping or mistyping either <c>expectedKind:</c> argument compiles
/// cleanly and silently reverts the route to redeem-anything, so the shims are invoked directly
/// here rather than re-testing the helper.
/// </para>
/// <para>
/// <c>FakeGate</c> / <c>FakePreviewStore</c> in <c>ToolDispatchTests</c> are private nested types,
/// so equivalents are redeclared below rather than shared.
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
        var gate = new FakeGate();
        var refactoring = new FakeRefactoring();
        var store = new FakePreviewStore(token: "tok-mfe-x", workspaceId: "ws-mfe-x")
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
        var gate = new FakeGate();
        var refactoring = new FakeRefactoring();
        var store = new FakePreviewStore(token: "tok-mfe-ok", workspaceId: "ws-mfe-ok")
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
        var gate = new FakeGate();
        var refactoring = new FakeRefactoring();
        // Kind left at its default (Unspecified) — an untagged producer or an out-of-tree store.
        var store = new FakePreviewStore(token: "tok-mfe-untagged", workspaceId: "ws-mfe-ut");

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
        var gate = new FakeGate();
        var refactoring = new FakeRefactoring();
        var store = new FakePreviewStore(token: "tok-ca-x", workspaceId: "ws-ca-x")
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
        var gate = new FakeGate();
        var refactoring = new FakeRefactoring();
        var store = new FakePreviewStore(token: "tok-ca-ok", workspaceId: "ws-ca-ok")
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
        var gate = new FakeGate();
        var refactoring = new FakeRefactoring();
        var store = new FakePreviewStore(token: "tok-ca-untagged", workspaceId: "ws-ca-ut");

        var result = await CodeActionTools.ApplyCodeAction(
            gate, refactoring, store, "tok-ca-untagged", CancellationToken.None);

        Assert.AreEqual(1, refactoring.ApplyCallCount,
            "Unspecified means 'no provenance claim' and must keep redeeming on a bound route.");
        StringAssert.Contains(result, "MultiFile.cs");
    }

    // ------------------------------------------------------------------
    // Fakes — equivalents of the private nested types in ToolDispatchTests.
    // ------------------------------------------------------------------

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
            => throw new NotSupportedException("Token apply must not route through RunLoadGateAsync.");

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException("Token apply must not call RemoveGate.");
    }

    private sealed class FakeRefactoring : IRefactoringService
    {
        public int ApplyCallCount { get; private set; }

        public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
        {
            ApplyCallCount++;
            return Task.FromResult(new ApplyResultDto(true, new[] { "MultiFile.cs" }, null));
        }

        public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, bool force, CancellationToken ct)
            => ApplyRefactoringAsync(previewToken, toolName, ct);

        public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, bool summary, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewFormatRangeAsync(string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewCodeFixAsync(string workspaceId, string diagnosticId, string filePath, int line, int column, string? fixId, CancellationToken ct)
            => throw new NotSupportedException();
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

        public PreviewKind Kind { get; init; } = PreviewKind.Unspecified;

        public string? PeekWorkspaceId(string token) => token == _token ? _workspaceId : null;

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

/// <summary>
/// preview-token-route-binding-multifile-codeaction: producer round-trip. Guards the
/// wrong-overload silent-drop risk — <c>IPreviewStore</c> carries several <c>Store</c> shapes and
/// the kind-discarding default body means tagging the WRONG one compiles but records
/// <see cref="PreviewKind.Unspecified"/>, which would leave the route permanently permissive
/// while the shim-level tests above still pass. These cases mint a real token through the real
/// producer and read the kind back off the real store.
/// </summary>
[DoNotParallelize]
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

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

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
