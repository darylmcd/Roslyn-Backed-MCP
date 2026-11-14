using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>symbol-refactor-preview-auto-applies-without-explicit-apply-call</c>
/// (gh #770). Pre-fix, <see cref="SymbolRefactorService.PreviewAsync"/> chained each
/// sub-operation by calling <c>RefactoringService.ApplyRefactoringAsync</c> between steps so
/// the next step could read the rewritten state from the live workspace — a side-effect that
/// wrote files to disk and recorded change-tracker entries before the agent ever called
/// <c>apply_composite</c>. The fix threads a single <see cref="Microsoft.CodeAnalysis.Solution"/>
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
///     entry whose <c>ToolName</c> is <c>apply_composite</c> (the inner-apply
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
        var retrieved = compositeStore.Retrieve(preview.PreviewToken, null);
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
        var retrieved = compositeStore.Retrieve(preview.PreviewToken, null);
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
        var dogDiff = preview.Changes.Single(change => change.FilePath == dogFilePath).UnifiedDiff;
        StringAssert.Contains(dogDiff, "-public class Dog");
        StringAssert.Contains(dogDiff, "+public class Puppy");
        StringAssert.Contains(dogDiff, "\"Yip\"");
    }

    [TestMethod]
    public async Task CompositePreview_OnlyReviewsAndAppliesTheRemainingPhysicalChanges()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var linkedPath = workspace.GetPath("SampleLib", "Linked.cs");
        var otherPath = workspace.GetPath("SampleLib", "Other.cs");
        var revertedPath = workspace.GetPath("SampleLib", "Reverted.cs");
        var appPath = workspace.GetPath("SampleApp", "SampleApp.csproj");
        const string linkedBefore = "namespace SampleLib;\npublic class Linked {}\n";
        const string otherBefore = "namespace SampleLib;\npublic class Other {}\n";
        const string revertedBefore = "namespace SampleLib;\npublic class Reverted {}\n";
        await File.WriteAllTextAsync(linkedPath, linkedBefore);
        await File.WriteAllTextAsync(otherPath, otherBefore);
        await File.WriteAllTextAsync(revertedPath, revertedBefore);
        var app = (await File.ReadAllTextAsync(appPath)).Replace("</Project>",
            "  <ItemGroup><Compile Include=\"../SampleLib/Linked.cs\" Link=\"Linked.cs\" /></ItemGroup>\n</Project>",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(appPath, app);
        var workspaceId = await workspace.LoadAsync();
        Assert.HasCount(2, WorkspaceManager.GetCurrentSolution(workspaceId).GetDocumentIdsWithFilePath(linkedPath));
        var store = new CompositePreviewStore();
        var preview = await CreateSymbolRefactorService(store).PreviewAsync(workspaceId,
            [
                new SymbolRefactorOperation(Kind: "edit", FileEdits: [new(linkedPath, [new(1, 1, 1, 1, "// linked edit\n")])]),
                new SymbolRefactorOperation(Kind: "rename", MetadataName: "SampleLib.Linked", NewName: "Combined"),
                new SymbolRefactorOperation(Kind: "edit", FileEdits:
                [
                    new(otherPath, [new(1, 1, 1, 1, "// retained\n")]),
                    new(revertedPath, [new(1, 1, 1, 1, "// temporary\n")]),
                ]),
                new SymbolRefactorOperation(Kind: "edit", FileEdits: [new(revertedPath, [new(1, 1, 2, 1, "")])]),
            ], CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { linkedPath, otherPath }, preview.Changes.Select(change => change.FilePath).ToArray());
        StringAssert.Contains(preview.Changes.Single(change => change.FilePath == linkedPath).UnifiedDiff, "+public class Combined {}");
        Assert.AreEqual(revertedBefore, await File.ReadAllTextAsync(revertedPath));
        Assert.AreEqual(linkedBefore, await File.ReadAllTextAsync(linkedPath));
        Assert.AreEqual(otherBefore, await File.ReadAllTextAsync(otherPath));

        var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, store, ChangeTracker);
        var applied = await orchestrator.ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        CollectionAssert.AreEquivalent(new[] { linkedPath, otherPath }, applied.AppliedFiles.ToArray());
        Assert.AreEqual(revertedBefore, await File.ReadAllTextAsync(revertedPath));
        StringAssert.Contains(preview.Changes.Single(change => change.FilePath == linkedPath).UnifiedDiff, "+// linked edit");
        Assert.AreEqual("// linked edit\n" + linkedBefore.Replace("class Linked", "class Combined", StringComparison.Ordinal),
            await File.ReadAllTextAsync(linkedPath));
        Assert.AreEqual("// retained\n" + otherBefore, await File.ReadAllTextAsync(otherPath));
    }

    [TestMethod]
    [DataRow(1, 17_000, DisplayName = "one file exceeds the per-file review cap")]
    [DataRow(5, 14_000, DisplayName = "multiple files exceed the aggregate review cap")]
    public async Task CompositePreview_RefusesATokenWhenChangesCannotBeReviewed(int fileCount, int commentLength)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var edits = new List<FileEditsDto>();
        for (var index = 0; index < fileCount; index++)
        {
            var path = workspace.GetPath("SampleLib", $"Budget{index}.cs");
            await File.WriteAllTextAsync(path, $"public class Budget{index} {{}}\n");
            edits.Add(new FileEditsDto(path, [new(1, 1, 1, 1, "// " + new string('x', commentLength) + "\n")]));
        }

        var workspaceId = await workspace.LoadAsync();
        var store = new CompositePreviewStore(maxEntries: 1);
        var priorToken = store.Store(workspaceId, WorkspaceManager.GetCurrentVersion(workspaceId), "prior preview", []);
        var service = CreateSymbolRefactorService(store);
        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewAsync(workspaceId, [new SymbolRefactorOperation(Kind: "edit", FileEdits: edits)], CancellationToken.None));
        StringAssert.Contains(refusal.Message, "review budget");
        Assert.IsNotNull(store.PeekWorkspaceId(priorToken), "A refused preview must not create a token and evict the existing entry.");
        for (var index = 0; index < fileCount; index++)
        {
            Assert.AreEqual($"public class Budget{index} {{}}\n", await File.ReadAllTextAsync(edits[index].FilePath));
        }
    }

    [TestMethod]
    [DataRow(500)]
    [DataRow(501)]
    public async Task CompositePreview_CapsAllFilesTouchedEvenWhenTheChangesAreReverted(int fileCount)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string originalIdentifier = "ClassWithEnoughCharactersToMakeTheIntermediateDiffLarge";
        var declarationPath = workspace.GetPath("SampleLib", "RenameTarget.cs");
        await File.WriteAllTextAsync(declarationPath, $"namespace SampleLib; public class {originalIdentifier} {{}}\n");
        for (var index = 1; index < fileCount; index++)
        {
            var path = workspace.GetPath("SampleLib", $"RenameReference{index:D3}.cs");
            await File.WriteAllTextAsync(path, $"namespace SampleLib; public class Reference{index} : {originalIdentifier} {{}}\n");
        }

        var workspaceId = await workspace.LoadAsync();
        var store = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(store);
        SymbolRefactorOperation[] operations =
        [
            new(Kind: "rename", MetadataName: "SampleLib." + originalIdentifier, NewName: "Replacement"),
            new(Kind: "rename", MetadataName: "SampleLib.Replacement", NewName: originalIdentifier),
        ];

        if (fileCount == 500)
        {
            var preview = await service.PreviewAsync(workspaceId, operations, CancellationToken.None);
            Assert.IsEmpty(preview.Changes);
            Assert.IsEmpty(store.PeekChangedPaths(preview.PreviewToken)!);
        }
        else
        {
            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.PreviewAsync(workspaceId, operations, CancellationToken.None));
            StringAssert.Contains(refusal.Message, "500-file diff cap");
        }

        Assert.AreEqual($"namespace SampleLib; public class {originalIdentifier} {{}}\n", await File.ReadAllTextAsync(declarationPath));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "valid supplementary character")]
    [DataRow(true, DisplayName = "split surrogate")]
    public async Task CompositePreview_RequiresTextThatTheTokenCanRepresent(bool splitSurrogate)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "UnicodeComment.cs");
        const string original = "// \U0001F680\npublic class UnicodeComment {}\n";
        await File.WriteAllTextAsync(path, original);
        var workspaceId = await workspace.LoadAsync();
        var store = new CompositePreviewStore(maxEntries: 1);
        var priorToken = store.Store(workspaceId, WorkspaceManager.GetCurrentVersion(workspaceId), "prior preview", []);
        var service = CreateSymbolRefactorService(store);
        var edit = splitSurrogate ? new TextEditDto(1, 4, 1, 5, "") : new TextEditDto(1, 6, 1, 6, " ok");
        SymbolRefactorOperation[] operations = [new(Kind: "edit", FileEdits: [new(path, [edit])])];

        if (splitSurrogate)
        {
            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewAsync(workspaceId, operations, CancellationToken.None));
            StringAssert.Contains(refusal.Message, "valid Unicode");
            Assert.IsNotNull(store.PeekWorkspaceId(priorToken));
            Assert.AreEqual(original, await File.ReadAllTextAsync(path));
        }
        else
        {
            var preview = await service.PreviewAsync(workspaceId, operations, CancellationToken.None);
            StringAssert.Contains(preview.Changes.Single().UnifiedDiff, "+// \U0001F680 ok");
            Assert.AreEqual(original, await File.ReadAllTextAsync(path));
            var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, store, ChangeTracker);
            var applied = await orchestrator.ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.AreEqual("// \U0001F680 ok\npublic class UnicodeComment {}\n", await File.ReadAllTextAsync(path));
        }
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

        // Redeem through CompositeApplyOrchestrator (the production path for apply_composite).
        var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, ChangeTracker);
        var applyResult = await orchestrator.ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);

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

        // Change tracker must show exactly ONE entry attributed to apply_composite.
        // Pre-fix, this assertion would have failed: each chained sub-op recorded its own
        // entry under the bogus "symbol_refactor_preview" tool name, polluting the ledger
        // with N inner-apply records before the outer redeem fired.
        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count,
            $"Exactly one ledger entry expected after composite redeem; got [{string.Join(", ", changes.Select(c => c.ToolName))}].");
        Assert.AreEqual("apply_composite", changes[0].ToolName,
            "The single ledger entry must be attributed to apply_composite, not the inner sub-op tool name.");
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
        var applyResult = await orchestrator.ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);

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
        var applyResult = await orchestrator.ApplyCompositeAsync(emptyToken, CompositeAdmission.AdmitAll, CancellationToken.None);

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
        Assert.IsNotNull(compositeStore.Retrieve(emptyToken, null),
            "The empty-mutations guard must NOT invalidate the token; callers should still be able to inspect it.");
    }


    [TestMethod]
    [DataRow("unknown", "Kind")]
    [DataRow("rename", "NewName")]
    [DataRow("edit", "FileEdits")]
    [DataRow("pattern", "Pattern")]
    [DataRow("goal", "Goal")]
    [DataRow("parse-pattern", "pattern")]
    [DataRow("parse-goal", "goal")]
    [DataRow("orphan", "goal")]
    [DataRow("kinds", "goal")]
    public async Task CompositeArgumentRefusal_PreservesSafeGuidanceAndOperationWireIdentity(string shape, string parameter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var service = CreateSymbolRefactorService(new CompositePreviewStore());
        var op = shape switch
        {
            "unknown" => new SymbolRefactorOperation("PRIVATE-kind"),
            "rename" => new SymbolRefactorOperation("rename"),
            "edit" => new SymbolRefactorOperation("edit"),
            "pattern" => new SymbolRefactorOperation("restructure", Pattern: " ", Goal: "42"),
            "goal" => new SymbolRefactorOperation("restructure", Pattern: "42"),
            "parse-pattern" => new SymbolRefactorOperation("restructure", Pattern: "PRIVATE_pattern +", Goal: "42"),
            "parse-goal" => new SymbolRefactorOperation("restructure", Pattern: "42", Goal: "PRIVATE_goal +"),
            "orphan" => new SymbolRefactorOperation("restructure", Pattern: "__captured__", Goal: "__PRIVATE_orphan__"),
            _ => new SymbolRefactorOperation("restructure", Pattern: "42", Goal: "return 42;"),
        };
        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewAsync(workspace.WorkspaceId, [op], CancellationToken.None));
        var inner = Assert.IsInstanceOfType<PublicArgumentException>(ex.InnerException);
        Assert.AreEqual(parameter, inner.ParamName);
        StringAssert.Contains(ex.PublicMessage, "operation #1");
        StringAssert.Contains(ex.PublicMessage, inner.PublicMessage);
        using var payload = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ex, "symbol_refactor_preview"));
        Assert.AreEqual("InvalidOperation", payload.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", payload.RootElement.GetProperty("exceptionType").GetString());
        Assert.AreEqual(ex.PublicMessage, payload.RootElement.GetProperty("message").GetString());
        Assert.IsFalse(ex.Message.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.IsFalse(payload.RootElement.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompositeNullOperation_RefusesJsonNullWithoutWrapperDereference()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var operations = JsonSerializer.Deserialize<SymbolRefactorOperation[]>("[null]")!;
        Assert.IsNull(operations[0]);
        var service = CreateSymbolRefactorService(new CompositePreviewStore());
        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewAsync(workspace.WorkspaceId, operations, CancellationToken.None));
        var inner = Assert.IsInstanceOfType<PublicArgumentException>(ex.InnerException);
        Assert.AreEqual("operations", inner.ParamName);
        StringAssert.Contains(ex.PublicMessage, "operation #1");
        StringAssert.Contains(ex.PublicMessage, "non-null");
        using var payload = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ex, "symbol_refactor_preview"));
        Assert.AreEqual("InvalidOperation", payload.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", payload.RootElement.GetProperty("exceptionType").GetString());
        Assert.AreEqual(ex.PublicMessage, payload.RootElement.GetProperty("message").GetString());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.PreviewAsync(workspace.WorkspaceId, operations, cancelled.Token));
    }

    [TestMethod]
    [DataRow("file", "sourceFilePath")]
    [DataRow("type", "sourceType")]
    [DataRow("empty", "partitions")]
    [DataRow("name", "partitions")]
    [DataRow("members", "partitions")]
    [DataRow("duplicate", "partitions")]
    [DataRow("null-entry", "partitions")]
    [DataRow("null-members", "partitions")]
    [DataRow("null-member", "partitions")]
    [DataRow("blank-member", "partitions")]
    public async Task SplitArguments_PublishSafePartitionRequirements(string shape, string parameter)
    {
        var partitions = shape switch
        {
            "empty" => Array.Empty<SplitServicePartition>(),
            "null-entry" => [null!],
            "null-members" => [new SplitServicePartition("PRIVATE_type", null!)],
            "null-member" => [new SplitServicePartition("PRIVATE_type", [null!])],
            "blank-member" => [new SplitServicePartition("PRIVATE_type", [" "])],
            "name" => [new SplitServicePartition(" ", ["PRIVATE_member"])],
            "members" => [new SplitServicePartition("PRIVATE_type", [])],
            "duplicate" => [new SplitServicePartition("PRIVATE_A", ["PRIVATE_member"]),
                new SplitServicePartition("PRIVATE_B", ["PRIVATE_member"])],
            _ => new[] { new SplitServicePartition("PRIVATE_type", ["PRIVATE_member"]) },
        };
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            CreateSymbolRefactorService(new CompositePreviewStore()).PreviewSplitServiceWithDiAsync(
                "unused-workspace", shape == "file" ? " " : "PRIVATE_path",
                shape == "type" ? " " : "PRIVATE_type", partitions, null, CancellationToken.None));
        Assert.AreEqual(parameter, ex.ParamName);
        using var payload = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ex, "split_service_with_di_preview"));
        Assert.AreEqual("InvalidArgument", payload.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentException", payload.RootElement.GetProperty("exceptionType").GetString());
        Assert.AreEqual(ex.PublicMessage, payload.RootElement.GetProperty("message").GetString());
        Assert.IsFalse(payload.RootElement.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
        StringAssert.Contains(ex.PublicMessage, shape switch
        {
            "duplicate" => "exactly one partition",
            "empty" => "At least one partition",
            "name" or "members" or "null-entry" or "null-members" or "null-member" or "blank-member" => "TypeName",
            _ => "required",
        });
    }

    [TestMethod]
    [DataRow("typeMetadataName", "")]
    [DataRow("typeMetadataName", " ")]
    [DataRow("newFieldName", "")]
    [DataRow("newFieldName", " ")]
    [DataRow("newFieldType", "")]
    [DataRow("newFieldType", " ")]
    public async Task RecordSatelliteArguments_PublishRequiredField(string parameter, string invalid)
    {
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
                "unused-workspace", parameter == "typeMetadataName" ? invalid : "PRIVATE_type",
                parameter == "newFieldName" ? invalid : "PRIVATE_field",
                parameter == "newFieldType" ? invalid : "PRIVATE_field_type", CancellationToken.None));
        Assert.AreEqual(parameter, ex.ParamName);
        using var payload = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ex, "record_field_add_with_satellites_preview"));
        Assert.AreEqual("InvalidArgument", payload.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentException", payload.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(ex.PublicMessage, parameter);
        Assert.IsFalse(payload.RootElement.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
    }


    [TestMethod]
    [DataRow("typeMetadataName")]
    [DataRow("newFieldName")]
    [DataRow("newFieldType")]
    public async Task RecordSatelliteNullArguments_PreserveBclWireIdentity(string parameter)
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
                "unused-workspace", parameter == "typeMetadataName" ? null! : "PRIVATE_type",
                parameter == "newFieldName" ? null! : "PRIVATE_field",
                parameter == "newFieldType" ? null! : "PRIVATE_field_type", CancellationToken.None));
        Assert.AreEqual(parameter, ex.ParamName);
        using var payload = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ex, "record_field_add_with_satellites_preview"));
        Assert.AreEqual("InvalidArgument", payload.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentNullException", payload.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(payload.RootElement.GetProperty("message").GetString(), parameter);
        Assert.IsFalse(payload.RootElement.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompositeCancellationAndOrdinaryFailure_KeepExistingClassification()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var service = CreateSymbolRefactorService(new CompositePreviewStore());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.PreviewAsync(workspace.WorkspaceId, [new SymbolRefactorOperation("PRIVATE-kind")], cancelled.Token));
        var ordinary = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.PreviewAsync(workspace.WorkspaceId,
                [new SymbolRefactorOperation("restructure", Pattern: "42", Goal: "43",
                    ScopeProjectName: "PRIVATE_missing_project")], CancellationToken.None));
        Assert.IsNotNull(ordinary.InnerException);
        using var payload = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ordinary, "symbol_refactor_preview"));
        Assert.AreEqual("InvalidOperation", payload.RootElement.GetProperty("category").GetString());
        Assert.IsFalse(payload.RootElement.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
    }

    private static SymbolRefactorService CreateSymbolRefactorService(CompositePreviewStore compositeStore)
    {
        // Mirrors CompositeSplitServiceDiPreviewTests.CreateSymbolRefactorService — we construct
        // a fresh RestructureService per test (the shared one in TestBase is not exposed) and
        // reuse the shared singletons for everything else.
        var restructureService = new RestructureService(WorkspaceManager, PreviewStore);
        return new SymbolRefactorService(
            WorkspaceManager,
            RefactoringService,
            EditService,
            restructureService,
            compositeStore,
            DiRegistrationService);
    }
}
