using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>symbol-refactor-preview-auto-applies-without-explicit-apply-call</c>
/// (gh #770). Pre-fix, <see cref="SymbolRefactorService.PreviewAsync"/> chained each
/// sub-operation by calling <c>RefactoringService.ApplyRefactoringAsync</c> between steps so
/// the next step could read the rewritten state from the live workspace — a side-effect that
/// wrote files to disk and recorded change-tracker entries before the agent ever called
/// <c>apply_composite_preview</c>. The fix threads a single <see cref="Microsoft.CodeAnalysis.Solution"/>
/// snapshot through the loop and stores the accumulated mutations under one composite-preview
/// token, deferring all disk writes until the apply call.
///
/// <para>
/// The three scenarios pinned here cover the validation requirements in the plan:
/// </para>
/// <list type="number">
///   <item><description>
///     Single rename op: preview alone leaves the on-disk file unchanged and the
///     <see cref="IChangeTracker"/> ledger empty for the workspace.
///   </description></item>
///   <item><description>
///     Two chained ops (rename then edit): same no-side-effect assertions after preview.
///   </description></item>
///   <item><description>
///     Redeem the composite token via <see cref="CompositeApplyOrchestrator.ApplyCompositeAsync"/>:
///     both edits land on disk AND <see cref="IChangeTracker.GetChanges"/> shows exactly ONE
///     entry whose <c>ToolName</c> is <c>apply_composite_preview</c> (the inner-apply
///     entries that polluted the ledger pre-fix are gone).
///   </description></item>
/// </list>
/// </summary>
[TestClass]
public sealed class SymbolRefactorPreviewTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SingleRenameOp_Preview_DoesNotWriteToDisk_AndChangeTrackerStaysEmpty()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        var dogFile = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");
        var dogFilePath = dogFile.FilePath!;
        var originalContent = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        ChangeTracker.Clear(wsId);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        var preview = await service.PreviewAsync(
            wsId,
            new[]
            {
                new SymbolRefactorOperation(
                    Kind: "rename",
                    MetadataName: "SampleLib.Dog",
                    NewName: "Puppy"),
            },
            CancellationToken.None);

        // Preview MUST be inert: file content on disk unchanged.
        var afterPreview = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalContent, afterPreview,
            "symbol_refactor_preview must NOT write to disk; the pre-fix auto-apply-each-step behavior is the regression.");

        // Change tracker MUST be empty (no inner apply fired).
        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(0, changes.Count,
            $"symbol_refactor_preview must NOT record any change-tracker entries pre-apply. Got: [{string.Join(", ", changes.Select(c => c.ToolName))}]");

        // Preview must still return a usable composite token.
        Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken),
            "A successful preview must return a non-empty composite token.");
        var retrieved = compositeStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(retrieved,
            "The returned token must be retrievable from the composite-preview store, not the legacy IPreviewStore.");
        Assert.IsTrue(retrieved.Value.Mutations.Count > 0,
            "The composite mutation list must contain at least one entry for the renamed file(s).");
        Assert.IsTrue(retrieved.Value.Mutations.Any(m =>
                string.Equals(m.FilePath, dogFilePath, StringComparison.OrdinalIgnoreCase)
                && m.UpdatedContent is not null
                && m.UpdatedContent.Contains("class Puppy", StringComparison.Ordinal)),
            "The rename must surface in the stored mutation set as updated text containing the new identifier.");
    }

    [TestMethod]
    public async Task ChainedRenameAndEditOps_Preview_DoesNotWriteToDisk_AndChangeTrackerStaysEmpty()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        var dogFile = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");
        var dogFilePath = dogFile.FilePath!;
        var originalContent = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        ChangeTracker.Clear(wsId);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        // Chain: (1) rename `Dog` → `Puppy`, (2) edit a line in the renamed-but-not-yet-applied
        // file. The edit's text-targeting still uses original-file coordinates because the
        // rename only renames identifiers; line/column geometry is unchanged.
        var preview = await service.PreviewAsync(
            wsId,
            new[]
            {
                new SymbolRefactorOperation(
                    Kind: "rename",
                    MetadataName: "SampleLib.Dog",
                    NewName: "Puppy"),
                new SymbolRefactorOperation(
                    Kind: "edit",
                    FileEdits: new[]
                    {
                        // Replace `"Woof"` on line 7 with `"Yip"` — covers the literal at column 24-30.
                        new FileEditsDto(
                            FilePath: dogFilePath,
                            Edits: new[]
                            {
                                new TextEditDto(
                                    StartLine: 7,
                                    StartColumn: 30,
                                    EndLine: 7,
                                    EndColumn: 36,
                                    NewText: "\"Yip\""),
                            }),
                    }),
            },
            CancellationToken.None);

        // Preview MUST be inert on disk and ledger.
        var afterPreview = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalContent, afterPreview,
            "Chained symbol_refactor_preview must NOT write to disk after either sub-operation.");

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(0, changes.Count,
            $"Chained symbol_refactor_preview must NOT record any change-tracker entries pre-apply. Got: [{string.Join(", ", changes.Select(c => c.ToolName))}]");

        Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken),
            "Chained preview must return a non-empty composite token.");
        var retrieved = compositeStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(retrieved, "Chained composite token must be retrievable.");

        // Stored mutation for Dog.cs must include BOTH the rename and the literal edit.
        var dogMutation = retrieved.Value.Mutations
            .FirstOrDefault(m => string.Equals(m.FilePath, dogFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(dogMutation,
            "The stored mutation set must include an entry for the chained-edit target file.");
        Assert.IsTrue(dogMutation.UpdatedContent!.Contains("class Puppy", StringComparison.Ordinal),
            "The rename from op#1 must appear in the chained mutation's text.");
        Assert.IsTrue(dogMutation.UpdatedContent.Contains("\"Yip\"", StringComparison.Ordinal),
            "The literal edit from op#2 must appear in the chained mutation's text.");
    }

    [TestMethod]
    public async Task RedeemCompositeToken_AppliesAllChainedEdits_AndChangeTrackerHasSingleEntry()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        var dogFile = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");
        var dogFilePath = dogFile.FilePath!;

        ChangeTracker.Clear(wsId);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        var preview = await service.PreviewAsync(
            wsId,
            new[]
            {
                new SymbolRefactorOperation(
                    Kind: "rename",
                    MetadataName: "SampleLib.Dog",
                    NewName: "Puppy"),
                new SymbolRefactorOperation(
                    Kind: "edit",
                    FileEdits: new[]
                    {
                        new FileEditsDto(
                            FilePath: dogFilePath,
                            Edits: new[]
                            {
                                new TextEditDto(
                                    StartLine: 7,
                                    StartColumn: 30,
                                    EndLine: 7,
                                    EndColumn: 36,
                                    NewText: "\"Yip\""),
                            }),
                    }),
            },
            CancellationToken.None);

        // Redeem through CompositeApplyOrchestrator (the production path for apply_composite_preview).
        var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, ChangeTracker);
        var applyResult = await orchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, $"Composite apply must succeed; error={applyResult.Error}");

        // symbol-refactor-preview-empty-appliedfiles-on-success (gh #750): a successful apply
        // must return at least one entry in AppliedFiles. Pre-fix, this assertion would have
        // passed (the foreach DID execute here because two ops produced mutations), but the
        // sibling new-test below catches the empty-mutations path that silently returned
        // success=true with appliedFiles=[]. Pinning both shapes guards against regression on
        // either side of the empty-mutations guard.
        Assert.IsTrue(applyResult.AppliedFiles.Count > 0,
            $"A successful composite apply must surface at least one entry in AppliedFiles; got count={applyResult.AppliedFiles.Count}.");

        // Both edits must be on disk now.
        var finalContent = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.IsTrue(finalContent.Contains("class Puppy", StringComparison.Ordinal),
            $"Rename from op#1 must land on disk after redeem. Content: {finalContent}");
        Assert.IsTrue(finalContent.Contains("\"Yip\"", StringComparison.Ordinal),
            $"Literal edit from op#2 must land on disk after redeem. Content: {finalContent}");

        // Change tracker must show exactly ONE entry attributed to apply_composite_preview.
        // Pre-fix, this assertion would have failed: each chained sub-op recorded its own
        // entry under the bogus "symbol_refactor_preview" tool name, polluting the ledger
        // with N inner-apply records before the outer redeem fired.
        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count,
            $"Exactly one ledger entry expected after composite redeem; got [{string.Join(", ", changes.Select(c => c.ToolName))}].");
        Assert.AreEqual("apply_composite_preview", changes[0].ToolName,
            "The single ledger entry must be attributed to apply_composite_preview, not the inner sub-op tool name.");
    }

    /// <summary>
    /// Regression for <c>symbol-refactor-preview-empty-appliedfiles-on-success</c> (gh #750).
    /// Pre-fix, <see cref="CompositeApplyOrchestrator.ApplyCompositeAsync"/> returned
    /// <c>{success: true, appliedFiles: []}</c> when the composite preview produced
    /// non-empty mutations OR empty mutations alike — empty was a silent success that
    /// masked the no-op. This test pins the happy path: a single-op preview that DOES
    /// produce mutations must surface them in <c>AppliedFiles</c> with the target file
    /// path present. The sibling empty-mutations path is tested separately below.
    /// </summary>
    [TestMethod]
    public async Task SingleRenameOp_ApplyComposite_AppliedFilesIsNonEmpty()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        var dogFile = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");
        var dogFilePath = dogFile.FilePath!;

        ChangeTracker.Clear(wsId);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        var preview = await service.PreviewAsync(
            wsId,
            new[]
            {
                new SymbolRefactorOperation(
                    Kind: "rename",
                    MetadataName: "SampleLib.Dog",
                    NewName: "Puppy"),
            },
            CancellationToken.None);

        var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, ChangeTracker);
        var applyResult = await orchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success,
            $"Single-rename composite apply must succeed; error={applyResult.Error}");
        Assert.IsTrue(applyResult.AppliedFiles.Count >= 1,
            $"AppliedFiles must contain at least one entry for the renamed file; got count={applyResult.AppliedFiles.Count}.");
        Assert.IsTrue(
            applyResult.AppliedFiles.Any(p => string.Equals(p, dogFilePath, StringComparison.OrdinalIgnoreCase)),
            $"AppliedFiles must include the rename target path '{dogFilePath}'; got [{string.Join(", ", applyResult.AppliedFiles)}].");
    }

    /// <summary>
    /// Regression for the breaking-change leg of <c>symbol-refactor-preview-empty-appliedfiles-on-success</c>
    /// (gh #750). When a composite preview produces an empty mutation list (e.g. a no-op
    /// solution-change walk), the orchestrator MUST surface an explicit failure instead of
    /// silently returning <c>{success: true, appliedFiles: []}</c>. This pins the new
    /// guard added at the top of <see cref="CompositeApplyOrchestrator.ApplyCompositeAsync"/>.
    /// </summary>
    [TestMethod]
    public async Task EmptyMutations_ApplyComposite_ReturnsFailureWithExplicitError()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        // Manually stage a composite-preview entry with an empty mutation list so we can
        // exercise the guard without depending on a Roslyn op that happens to no-op.
        var compositeStore = new CompositePreviewStore();
        var emptyToken = compositeStore.Store(
            wsId,
            workspaceVersion: WorkspaceManager.GetCurrentVersion(wsId),
            description: "empty-mutations regression fixture",
            mutations: Array.Empty<CompositeFileMutation>());

        var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, ChangeTracker);
        var applyResult = await orchestrator.ApplyCompositeAsync(emptyToken, CancellationToken.None);

        Assert.IsFalse(applyResult.Success,
            "Empty-mutations composite apply must NOT silently return success=true; the pre-fix behavior is the regression.");
        Assert.AreEqual(0, applyResult.AppliedFiles.Count,
            "An empty-mutations apply must return an empty AppliedFiles list (no files were touched).");
        Assert.IsNotNull(applyResult.Error,
            "An empty-mutations apply must surface an explicit error string so callers can disambiguate from a real apply.");
        Assert.IsTrue(
            applyResult.Error!.Contains("no file mutations", StringComparison.OrdinalIgnoreCase),
            $"Error string must reference the no-mutations cause; got '{applyResult.Error}'.");

        // Token must remain valid for caller inspection (Invalidate is skipped on this path).
        Assert.IsNotNull(compositeStore.Retrieve(emptyToken),
            "The empty-mutations guard must NOT invalidate the token; callers should still be able to inspect it.");
    }

    private static SymbolRefactorService CreateSymbolRefactorService(CompositePreviewStore compositeStore)
    {
        // Mirrors CompositeSplitServiceDiPreviewTests.CreateSymbolRefactorService — we construct
        // a fresh RestructureService per test (the shared one in TestBase is not exposed) and
        // reuse the shared singletons for everything else.
        var restructureService = new RestructureService(WorkspaceManager, PreviewStore);
        return new SymbolRefactorService(
            WorkspaceManager,
            PreviewStore,
            RefactoringService,
            EditService,
            restructureService,
            compositeStore,
            DiRegistrationService);
    }
}
