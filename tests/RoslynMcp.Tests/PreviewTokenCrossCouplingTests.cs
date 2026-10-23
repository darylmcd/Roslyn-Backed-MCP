using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>preview-token-cross-coupling-bundle</c> (closes
/// <c>dr-9-6-preview-token-issued-in-turn-t-invalidated-by-a</c> + FLAG
/// <c>severity-flag-unexpected-coupling-of-preview-tokens</c>). Before the fix, the
/// <see cref="PreviewStore"/> shared a single workspace-version stamp across all tokens
/// and <see cref="WorkspaceManager.TryApplyChanges"/> called <c>InvalidateAll</c> on every
/// successful apply — so any sibling <c>*_apply</c> in the same turn would invalidate
/// every outstanding preview token. The fix captures an isolated per-token
/// <see cref="Microsoft.CodeAnalysis.Solution"/> snapshot pair (<c>OriginalSolution</c> +
/// <c>ModifiedSolution</c>) and rebases the preview's intended diff onto the current
/// workspace at apply time.
/// </summary>
[TestClass]
public sealed class PreviewTokenCrossCouplingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    /// <summary>
    /// The exact repro from NetworkDocumentation audit 2026-04-15 §9.6: two previews are
    /// issued in turn T (different files); token A is applied; token B must still be
    /// valid and its apply must succeed.
    /// </summary>
    [TestMethod]
    public async Task Sibling_Apply_Does_Not_Invalidate_Unrelated_Preview_Token()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var firstFilePath = workspace.GetPath("SampleLib", "Generated", "FirstCoupled.cs");
        var secondFilePath = workspace.GetPath("SampleLib", "Generated", "SecondCoupled.cs");

        // Issue TWO previews against the same workspace for UNRELATED files.
        var previewA = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                firstFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class FirstCoupled\n{\n}\n"),
            CancellationToken.None);

        var previewB = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                secondFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class SecondCoupled\n{\n}\n"),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(previewA.PreviewToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(previewB.PreviewToken));
        Assert.AreNotEqual(previewA.PreviewToken, previewB.PreviewToken);

        // Apply A first — this bumps the workspace version. Pre-fix, the `TryApplyChanges`
        // path called `_previewStore.InvalidateAll(workspaceId)`, so token B would be
        // dropped here even though nothing about file B changed.
        var applyA = await RefactoringService.ApplyRefactoringAsync(previewA.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyA.Success, applyA.Error);
        Assert.IsTrue(File.Exists(firstFilePath));

        // Apply B — must still succeed. Pre-fix this returned
        // "Preview token is invalid, expired, or stale because the workspace changed
        // since the preview was generated." (audit §9.6, §5 "extract_interface_apply
        // stale preview token (invalidated by sibling format_range_apply in same turn)").
        var applyB = await RefactoringService.ApplyRefactoringAsync(previewB.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyB.Success,
            $"Sibling apply must not invalidate unrelated preview tokens. Got: {applyB.Error}");
        Assert.IsTrue(File.Exists(secondFilePath));
        StringAssert.Contains(
            await File.ReadAllTextAsync(secondFilePath, CancellationToken.None),
            "class SecondCoupled");

        // Both files must be present in the current workspace solution after the
        // second apply — the rebase path must have mirrored A's addition forward
        // without reverting it.
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.IsNotNull(SymbolResolver.FindDocument(solution, firstFilePath),
            "Token A's file must remain in the workspace after token B's apply.");
        Assert.IsNotNull(SymbolResolver.FindDocument(solution, secondFilePath),
            "Token B's file must be present after its own apply.");
    }

    /// <summary>
    /// Mirror scenario with text-edit previews (no added/removed documents). Two previews
    /// organize usings on two different files; applying one must not invalidate the other.
    /// Guards against the second failure mode — passing the stale
    /// <see cref="Microsoft.CodeAnalysis.Solution"/> to <c>TryApplyChanges</c> and having
    /// <c>GetChanges</c> treat unrelated sibling edits as reversions.
    /// </summary>
    [TestMethod]
    public async Task Sibling_Text_Edit_Apply_Does_Not_Invalidate_Other_Text_Edit_Token()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var firstFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var secondFilePath = workspace.GetPath("SampleApp", "Program.cs");

        // Skip this shape when the fixtures diverge from expectations so the test fails
        // loudly at assertion time on real regressions but doesn't spuriously fail on a
        // fixture rename.
        Assert.IsTrue(File.Exists(firstFilePath), $"Fixture file missing: {firstFilePath}");
        Assert.IsTrue(File.Exists(secondFilePath), $"Fixture file missing: {secondFilePath}");

        var previewA = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId, firstFilePath, CancellationToken.None);
        var previewB = await RefactoringService.PreviewOrganizeUsingsAsync(
            workspace.WorkspaceId, secondFilePath, CancellationToken.None);

        // OrganizeUsings may emit no-op previews on a clean fixture; both tests above
        // only enforce "token-B-remains-valid-after-A's-apply". If either preview is
        // empty we still exercise the rebase path by invoking apply.
        Assert.IsFalse(string.IsNullOrWhiteSpace(previewA.PreviewToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(previewB.PreviewToken));

        var applyA = await RefactoringService.ApplyRefactoringAsync(previewA.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyA.Success, applyA.Error);

        var applyB = await RefactoringService.ApplyRefactoringAsync(previewB.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyB.Success,
            $"Sibling text-edit apply must not invalidate unrelated preview tokens. Got: {applyB.Error}");

        // The file from token A must still exist on disk (it must not have been
        // reverted by B's rebase overwriting the workspace with the pre-A snapshot).
        Assert.IsTrue(File.Exists(firstFilePath), "Token A's file must remain on disk after token B's apply.");
    }

    [TestMethod]
    public async Task Sibling_Apply_Preserves_Project_Reference_From_Stale_CrossProject_Preview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await workspace.LoadAsync(CancellationToken.None);

        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var sourceProjectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var unrelatedFilePath = workspace.GetPath("SampleApp", "UnrelatedSibling.cs");

        var crossProjectPreview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);
        var unrelatedPreview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleApp",
                unrelatedFilePath,
                "namespace SampleApp;\n\ninternal sealed class UnrelatedSibling { }\n"),
            CancellationToken.None);

        var unrelatedApply = await RefactoringService.ApplyRefactoringAsync(
            unrelatedPreview.PreviewToken,
            "test_apply",
            CancellationToken.None);
        Assert.IsTrue(unrelatedApply.Success, unrelatedApply.Error);

        var crossProjectApply = await RefactoringService.ApplyRefactoringAsync(
            crossProjectPreview.PreviewToken,
            "test_apply",
            CancellationToken.None);
        Assert.IsTrue(crossProjectApply.Success, crossProjectApply.Error);

        var projectXml = XDocument.Load(sourceProjectFilePath);
        Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
            string.Equals(
                Path.GetFileName((string?)element.Attribute("Include")),
                "Contracts.csproj",
                StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    [DataRow("aliases", false, false)]
    [DataRow("embed", false, false)]
    [DataRow("remove", false, false)]
    [DataRow("add", false, false)]
    [DataRow("aliases", true, false)]
    [DataRow("embed", true, false)]
    [DataRow("remove", true, false)]
    [DataRow("add", true, false)]
    [DataRow("aliases", false, true)]
    [DataRow("remove", false, true)]
    public async Task Stale_ProjectReference_Preview_Preserves_Metadata_And_Sibling_Changes(
        string transition, bool siblingAlreadyAppliedDesiredReference, bool siblingChangesOtherReference)
    {
        await using var workspace = await CreateReferenceWorkspaceAsync(transition != "add");
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var desired = ChangeReference(original, "Contracts", DesiredReference(original, transition));
        var token = StoreReferencePreview(workspace, desired);

        var sibling = siblingAlreadyAppliedDesiredReference ? desired : original;
        var expectedOtherReference = Reference(original, "OtherContracts",
            siblingChangesOtherReference ? "SiblingAlias" : "OriginalOther", siblingChangesOtherReference);
        if (siblingChangesOtherReference)
        {
            sibling = ChangeReference(sibling, "OtherContracts", expectedOtherReference);
        }
        var sourcePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var source = SymbolResolver.FindDocument(sibling, sourcePath)!;
        var siblingText = (await source.GetTextAsync()).ToString() + "\n// Sibling edit must survive reference rebase.\n";
        sibling = sibling.WithDocumentText(source.Id, SourceText.From(siblingText));
        var siblingResult = await RefactoringService.ApplyRefactoringAsync(
            StoreReferencePreview(workspace, sibling), "test_apply", CancellationToken.None);
        Assert.IsTrue(siblingResult.Success, siblingResult.Error);
        await AssertReferenceStateAsync(workspace, "OtherContracts", expectedOtherReference);
        var projectBytesAfterSibling = await File.ReadAllBytesAsync(workspace.GetPath("SampleLib", "SampleLib.csproj"));

        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);

        Assert.IsTrue(result.Success, result.Error);
        await AssertReferenceStateAsync(workspace, "Contracts", DesiredReference(original, transition));
        await AssertReferenceStateAsync(workspace, "OtherContracts", expectedOtherReference);
        Assert.AreEqual(siblingText, await File.ReadAllTextAsync(sourcePath));
        var currentSource = SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), sourcePath)!;
        Assert.AreEqual(siblingText, (await currentSource.GetTextAsync()).ToString());
        if (siblingAlreadyAppliedDesiredReference)
        {
            CollectionAssert.AreEqual(projectBytesAfterSibling,
                await File.ReadAllBytesAsync(workspace.GetPath("SampleLib", "SampleLib.csproj")),
                "An already applied reference delta must not rewrite the project file.");
        }
    }

    [TestMethod]
    [DataRow("aliases")]
    [DataRow("remove")]
    [DataRow("add")]
    [DataRow("remove-embed-only")]
    public async Task Conflicting_ProjectReference_Preview_Rejects_Before_Source_Or_Create_Effects(string transition)
    {
        await using var workspace = await CreateReferenceWorkspaceAsync(transition != "add");
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var sourcePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var createdPath = workspace.GetPath("SampleLib", "PendingReferenceCreate.cs");
        var source = SymbolResolver.FindDocument(original, sourcePath)!;
        var pending = ChangeReference(original, "Contracts", DesiredReference(original, transition == "remove-embed-only" ? "remove" : transition))
            .WithDocumentText(source.Id, SourceText.From((await source.GetTextAsync()).ToString() + "\n// Pending source effect.\n"))
            .AddDocument(DocumentId.CreateNewId(source.Project.Id), "PendingReferenceCreate.cs",
                SourceText.From("namespace SampleLib; internal class PendingReferenceCreate {}\n"), filePath: createdPath);
        var token = StoreReferencePreview(workspace, pending);
        var divergentReference = Reference(original, "Contracts", transition == "remove-embed-only" ? "OriginalAlias" : "DivergentAlias", true);
        var sibling = ChangeReference(original, "Contracts", divergentReference);
        var siblingResult = await RefactoringService.ApplyRefactoringAsync(
            StoreReferencePreview(workspace, sibling), "test_apply", CancellationToken.None);
        Assert.IsTrue(siblingResult.Success, siblingResult.Error);
        await AssertReferenceStateAsync(workspace, "Contracts", divergentReference);
        var current = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var referenceSnapshot = ReferenceSnapshot(current);

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
            await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None));
        var json = ToolErrorHandler.ClassifyAndFormat(error, "refactoring_apply");
        using var envelope = JsonDocument.Parse(json);
        var publishedMessage = envelope.RootElement.GetProperty("message").GetString()!;
        Assert.IsTrue(envelope.RootElement.GetProperty("error").GetBoolean(), json);
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString(), json);
        Assert.AreEqual(error.PublicMessage, publishedMessage, json);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error.PublicMessage));
        StringAssert.Contains(error.PublicMessage, "project reference");
        StringAssert.Contains(error.PublicMessage, "preview");
        foreach (var path in new[] { workspace.RootPath, sourcePath, projectPath, createdPath })
        {
            Assert.IsFalse(publishedMessage.Contains(path, StringComparison.OrdinalIgnoreCase), json);
            Assert.IsFalse(publishedMessage.Contains(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase), json);
        }
        Assert.IsFalse(json.Contains("stackTrace", StringComparison.Ordinal), json);

        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        Assert.IsFalse(File.Exists(createdPath), "Conflict rejection must precede file creation.");
        var after = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        CollectionAssert.AreEqual(referenceSnapshot, ReferenceSnapshot(after));
        Assert.AreEqual((await SymbolResolver.FindDocument(current, sourcePath)!.GetTextAsync()).ToString(),
            (await SymbolResolver.FindDocument(after, sourcePath)!.GetTextAsync()).ToString());
        Assert.IsNull(SymbolResolver.FindDocument(after, createdPath));
    }

    [TestMethod]
    [DataRow("alias-order-base")]
    [DataRow("alias-order-desired")]
    [DataRow("implicit-global")]
    public async Task Reloaded_ProjectReference_Metadata_Uses_Semantic_Alias_Equality(string shape)
    {
        var originalAliases = shape == "implicit-global" ? "" : shape == "alias-order-desired" ? "OriginalAlias" : "Alpha,Beta";
        await using var workspace = await CreateReferenceWorkspaceAsync(true, originalAliases);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var desiredAliases = shape == "alias-order-desired" ? "Alpha,Beta" : "DesiredAlias";
        var desired = Reference(original, "Contracts", desiredAliases, false);
        var token = StoreReferencePreview(workspace, ChangeReference(original, "Contracts", desired));
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Load(projectPath);
        var edge = xml.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Replace('\\', '/').EndsWith("/Contracts.csproj", StringComparison.Ordinal) == true);
        edge.SetElementValue("Aliases", shape == "implicit-global" ? "global" : "Beta,Alpha");
        edge.SetElementValue("EmbedInteropTypes", "false");
        xml.Save(projectPath);
        await workspace.ReloadAsync();
        var current = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        if (shape == "alias-order-desired")
        {
            await AssertReferenceStateAsync(workspace, "Contracts", desired);
        }
        ChangeTracker.Clear(workspace.WorkspaceId);

        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);

        Assert.IsTrue(result.Success, result.Error);
        var changes = ChangeTracker.GetChanges(workspace.WorkspaceId);
        Assert.HasCount(1, changes, "A successful apply must retain its tool audit record even when already satisfied.");
        Assert.AreEqual("test_apply", changes[0].ToolName);
        if (shape == "alias-order-desired")
        {
            Assert.AreSame(current, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
            Assert.AreEqual(0, result.AppliedFiles.Count);
            Assert.IsNull(PreviewStore.Retrieve(token));
        }
        await AssertReferenceStateAsync(workspace, "Contracts", desired);
        await AssertReferenceStateAsync(workspace, "OtherContracts", Reference(original, "OtherContracts", "OriginalOther", false));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Already_Satisfied_Apply_Undo_Does_Not_Revert_Earlier_Or_Later_Real_Apply(bool applyLaterEdit)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var path = workspace.GetPath("SampleLib", "Dog.cs");
        var document = SymbolResolver.FindDocument(original, path)!;
        var originalText = (await document.GetTextAsync()).ToString();
        var editedText = originalText + "\n// Earlier real apply must survive no-op undo.\n";
        ChangeTracker.Clear(workspace.WorkspaceId);
        var first = await RefactoringService.ApplyRefactoringAsync(
            StoreReferencePreview(workspace, original.WithDocumentText(document.Id, SourceText.From(editedText))),
            "real_apply", CancellationToken.None);
        Assert.IsTrue(first.Success, first.Error);

        var current = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var noOp = await RefactoringService.ApplyRefactoringAsync(
            StoreReferencePreview(workspace, current), "noop_apply", CancellationToken.None);
        Assert.IsTrue(noOp.Success, noOp.Error);
        Assert.HasCount(0, noOp.AppliedFiles);
        var changes = ChangeTracker.GetChanges(workspace.WorkspaceId);
        Assert.HasCount(2, changes);
        Assert.AreEqual("noop_apply", changes[1].ToolName);

        var laterPath = workspace.GetPath("SampleLib", "Cat.cs");
        var laterOriginal = await File.ReadAllTextAsync(laterPath);
        var laterText = laterOriginal + "\n// Later real apply must survive no-op undo.\n";
        int? laterSequence = null;
        if (applyLaterEdit)
        {
            var beforeLater = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
            var laterDocument = SymbolResolver.FindDocument(beforeLater, laterPath)!;
            Assert.AreEqual(laterOriginal, (await laterDocument.GetTextAsync()).ToString());
            var later = await RefactoringService.ApplyRefactoringAsync(
                StoreReferencePreview(workspace, beforeLater.WithDocumentText(laterDocument.Id, SourceText.From(laterText))),
                "later_apply", CancellationToken.None);
            Assert.IsTrue(later.Success, later.Error);
            laterSequence = ChangeTracker.GetChanges(workspace.WorkspaceId).Last().SequenceNumber;
        }
        var beforeNoOpUndo = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var beforeNoOpUndoVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);

        var undoNoOp = await UndoService.RevertBySequenceAsync(
            workspace.WorkspaceId, changes[1].SequenceNumber, CancellationToken.None);
        Assert.IsTrue(undoNoOp.Reverted, undoNoOp.Reason);
        Assert.AreEqual(editedText, await File.ReadAllTextAsync(path));
        var afterNoOpUndo = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreSame(beforeNoOpUndo, afterNoOpUndo);
        Assert.AreEqual(beforeNoOpUndoVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.AreEqual(editedText, (await SymbolResolver.FindDocument(afterNoOpUndo, path)!.GetTextAsync()).ToString());
        Assert.AreEqual(applyLaterEdit ? laterText : laterOriginal, await File.ReadAllTextAsync(laterPath));
        Assert.AreEqual(applyLaterEdit ? laterText : laterOriginal,
            (await SymbolResolver.FindDocument(afterNoOpUndo, laterPath)!.GetTextAsync()).ToString());

        if (laterSequence is { } sequence)
        {
            var undoLater = await UndoService.RevertBySequenceAsync(workspace.WorkspaceId, sequence, CancellationToken.None);
            Assert.IsTrue(undoLater.Reverted, undoLater.Reason);
            Assert.AreEqual(laterOriginal, await File.ReadAllTextAsync(laterPath));
            Assert.AreEqual(editedText, await File.ReadAllTextAsync(path));
        }

        var undoFirst = await UndoService.RevertBySequenceAsync(
            workspace.WorkspaceId, changes[0].SequenceNumber, CancellationToken.None);
        Assert.IsTrue(undoFirst.Reverted, undoFirst.Reason);
        Assert.AreEqual(originalText, await File.ReadAllTextAsync(path));
    }

    private static async Task<IsolatedWorkspaceScope> CreateReferenceWorkspaceAsync(bool includeContracts, string aliases = "OriginalAlias")
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        return await InitializeWithCleanupAsync(workspace, async (scope, ct) =>
        {
            foreach (var name in new[] { "Contracts", "OtherContracts" })
            {
                AddProjectToCopiedSolution(scope.RootPath, name, "net10.0");
                await File.WriteAllTextAsync(scope.GetPath(name, "Interop.cs"),
                    "using System.Runtime.InteropServices;\n" +
                    "[assembly: ImportedFromTypeLib(\"ReferenceFixture\")]\n" +
                    "[assembly: Guid(\"5697EF2B-1751-4311-8D46-04636AF03FE6\")]\n" +
                    "namespace " + name + ";\n" +
                    "[ComImport, Guid(\"9E865C62-3092-4542-B07C-8097DB911D32\"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]\n" +
                    "public interface IContract { void Invoke(); }\n", ct);
            }
            var projectPath = scope.GetPath("SampleLib", "SampleLib.csproj");
            var xml = XDocument.Load(projectPath);
            var references = new XElement("ItemGroup", ReferenceXml("OtherContracts", "OriginalOther", false));
            if (includeContracts)
            {
                references.Add(ReferenceXml("Contracts", aliases, false));
            }
            xml.Root!.Add(references);
            xml.Save(projectPath);
            await scope.LoadAsync(ct);
        });
    }

    private static XElement ReferenceXml(string name, string aliases, bool embed) =>
        new("ProjectReference", new XAttribute("Include", $"../{name}/{name}.csproj"),
            new XElement("Aliases", aliases), new XElement("EmbedInteropTypes", embed ? "true" : "false"));

    private static ProjectReference Reference(Solution solution, string name, string aliases, bool embed) =>
        new(solution.Projects.Single(project => project.Name == name).Id,
            aliases.Length == 0 ? [] : [.. aliases.Split(',')], embed);

    private static ProjectReference? DesiredReference(Solution original, string transition) => transition switch
    {
        "aliases" => Reference(original, "Contracts", "DesiredAlias", false),
        "embed" => Reference(original, "Contracts", "OriginalAlias", true),
        "remove" => null,
        "add" => Reference(original, "Contracts", "DesiredAlias", true),
        _ => throw new AssertFailedException($"Unknown reference transition: {transition}")
    };

    private static Solution ChangeReference(Solution solution, string name, ProjectReference? desired)
    {
        var source = solution.Projects.Single(project => project.Name == "SampleLib");
        var target = solution.Projects.Single(project => project.Name == name);
        return solution.WithProjectReferences(source.Id,
            source.ProjectReferences.Where(reference => reference.ProjectId != target.Id)
                .Concat(desired is null ? [] : new[] { desired }));
    }

    private static string StoreReferencePreview(IsolatedWorkspaceScope workspace, Solution modified) =>
        PreviewStore.Store(workspace.WorkspaceId, modified,
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "Project reference regression");

    private static string[] NormalizedAliases(IEnumerable<string> aliases)
    {
        var values = aliases.ToArray();
        return values.Length == 0 ? ["global"] : values.Order(StringComparer.Ordinal).ToArray();
    }

    private static string[] ReferenceSnapshot(Solution solution) =>
        solution.Projects.Single(project => project.Name == "SampleLib").ProjectReferences
            .Select(reference => $"{solution.GetProject(reference.ProjectId)!.FilePath}|{string.Join(',', NormalizedAliases(reference.Aliases))}|{reference.EmbedInteropTypes}")
            .Order(StringComparer.Ordinal).ToArray();

    private static async Task AssertReferenceStateAsync(IsolatedWorkspaceScope workspace, string target, ProjectReference? expected)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var targetId = solution.Projects.Single(project => project.Name == target).Id;
        var references = solution.Projects.Single(project => project.Name == "SampleLib").ProjectReferences
            .Where(reference => reference.ProjectId == targetId).ToArray();
        var xml = XDocument.Parse(await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "SampleLib.csproj")));
        var edges = xml.Descendants("ProjectReference").Where(element =>
            ((string?)element.Attribute("Include"))?.Replace('\\', '/').EndsWith($"/{target}.csproj", StringComparison.Ordinal) == true).ToArray();
        Assert.AreEqual(expected is null ? 0 : 1, references.Length, $"Workspace reference count for {target}");
        Assert.AreEqual(expected is null ? 0 : 1, edges.Length, $"Persisted reference count for {target}");
        if (expected is null)
        {
            return;
        }
        CollectionAssert.AreEqual(NormalizedAliases(expected.Aliases), NormalizedAliases(references[0].Aliases));
        Assert.AreEqual(expected.EmbedInteropTypes, references[0].EmbedInteropTypes);
        var persistedAliases = (string?)edges[0].Element("Aliases") ?? (string?)edges[0].Attribute("Aliases") ?? "";
        CollectionAssert.AreEqual(NormalizedAliases(expected.Aliases),
            NormalizedAliases(persistedAliases.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        var persistedEmbed = (string?)edges[0].Element("EmbedInteropTypes") ?? (string?)edges[0].Attribute("EmbedInteropTypes");
        Assert.AreEqual(expected.EmbedInteropTypes, string.Equals(persistedEmbed, "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// format-range-apply-preview-token-lifetime: a SINGLE reload no longer wipes live
    /// tokens. <see cref="PreviewStore.DefaultMaxVersionSpan"/> = 1 means a token stored at
    /// version V survives one auto-reload bump to V+1. The apply path's
    /// <c>RebaseModifiedSolutionOntoCurrentAsync</c> replays the captured diff onto the
    /// post-reload solution, so the file-creation produced by the preview lands on disk
    /// even though the underlying <c>MSBuildWorkspace</c> was disposed during reload.
    /// </summary>
    [TestMethod]
    public async Task Workspace_Reload_Within_Pinned_Range_Keeps_Token_Redeemable()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "ReloadInvalidates.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                targetFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class ReloadInvalidates { }\n"),
            CancellationToken.None);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

        // Single reload bumps the workspace version once — within DefaultMaxVersionSpan=1,
        // so InvalidateOnVersionBump leaves this token alone.
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success,
            $"Token stored at V should survive a single reload to V+1 with DefaultMaxVersionSpan=1. Error: {applyResult.Error}");
        Assert.IsTrue(File.Exists(targetFilePath), "Apply must persist the file even after one intervening reload.");
    }

    /// <summary>
    /// format-range-apply-preview-token-lifetime: bounded-range guarantee — a SECOND reload
    /// pushes the workspace version past the pinned ceiling
    /// (V + <see cref="PreviewStore.DefaultMaxVersionSpan"/> = V + 1), so the token MUST
    /// drop and the apply path must surface the "stale" rejection. Guards against the range
    /// policy regressing into "tokens never expire on reload."
    /// </summary>
    [TestMethod]
    public async Task Workspace_Reload_Twice_Drops_Token_And_Surfaces_Stale_Error()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "TwoReloadsDrop.cs");

        var preview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(
                "SampleLib",
                targetFilePath,
                "namespace SampleLib.Generated;\n\npublic sealed class TwoReloadsDrop { }\n"),
            CancellationToken.None);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

        // Two reload bumps push version past the pinned ceiling — token gets dropped.
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);
        await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsFalse(applyResult.Success, "Two reloads must push past the pinned ceiling and drop the token.");
        StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        Assert.IsFalse(File.Exists(targetFilePath), "Dropped token must not mutate disk.");
    }
}
