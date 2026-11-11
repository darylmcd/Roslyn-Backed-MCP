using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression: <c>preview_multi_file_edit</c> with default <c>skipSyntaxCheck=false</c> must
/// not emit a clean preview for parser-recovery shapes (Jellyfin F17: file-scoped namespace,
/// line comment, stray brace) that previously passed <c>GetCSharpSyntaxErrors</c> when the
/// tree only surfaced non-Error severities or skipped text without a severed Error diagnostic.
/// </summary>
[TestClass]
public sealed class PreviewMultiFileEditSyntaxRegressionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("preview")]
    [DataRow("direct")]
    public async Task LegalDirectiveMessages_DoNotBlockValidInsertions(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var target = workspace.GetPath("SampleLib", "RegionEditTarget.cs");
        var other = workspace.GetPath("SampleLib", "OtherEditTarget.cs");
        const string source = "#region Public members\n#warning intentional fixture warning\npublic class RegionEditTarget\n{\n}\n#endregion Public members\n";
        const string otherSource = "public class OtherEditTarget {}\n";
        const string property = "    public int Count { get; init; }\n";
        const string comment = "// insertion\n";
        await File.WriteAllTextAsync(target, source);
        await File.WriteAllTextAsync(other, otherSource);
        await workspace.LoadAsync();
        var server = await GetPathAuthorizedServerAsync();
        TextEditDto[] edits = [new(1, 1, 1, 1, comment), new(5, 1, 5, 1, property)];
        if (route == "preview")
        {
            var previewJson = await MultiFileEditTools.PreviewMultiFileEdit(server, WorkspaceExecutionGate, EditService,
                workspace.WorkspaceId, [new(target, edits), new(other, [new(1, 1, 1, 1, comment)])]);
            using var preview = JsonDocument.Parse(previewJson);
            var token = preview.RootElement.GetProperty("previewToken").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(token));
            var changes = preview.RootElement.GetProperty("changes").EnumerateArray().ToArray();
            Assert.HasCount(2, changes);
            var targetDiff = changes.Single(change => change.GetProperty("filePath").GetString() == target)
                .GetProperty("unifiedDiff").GetString()!;
            StringAssert.Contains(targetDiff, "+" + comment.TrimEnd());
            StringAssert.Contains(targetDiff, "+" + property.TrimEnd());
            Assert.AreEqual(source, await File.ReadAllTextAsync(target));
            Assert.AreEqual(otherSource, await File.ReadAllTextAsync(other));

            var applyJson = await MultiFileEditTools.ApplyMultiFilePreview(WorkspaceExecutionGate, RefactoringService,
                PreviewStore, token!);
            using var apply = JsonDocument.Parse(applyJson);
            Assert.IsTrue(apply.RootElement.GetProperty("success").GetBoolean(), applyJson);
            Assert.AreEqual(comment + otherSource, await File.ReadAllTextAsync(other));
        }
        else
        {
            var applyJson = await EditTools.ApplyTextEdit(server, WorkspaceExecutionGate, EditService,
                workspace.WorkspaceId, target, edits);
            using var apply = JsonDocument.Parse(applyJson);
            Assert.IsTrue(apply.RootElement.GetProperty("success").GetBoolean(), applyJson);
            Assert.AreEqual(otherSource, await File.ReadAllTextAsync(other));
        }

        Assert.AreEqual(comment + source.Replace("\n}\n", "\n" + property + "}\n", StringComparison.Ordinal),
            await File.ReadAllTextAsync(target));
    }

    [TestMethod]
    public async Task PreviewMultiFile_RejectsDiagnosticFreeSkippedTokensInsideStructuredTrivia()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var first = workspace.GetPath("SampleLib", "Dog.cs");
        var second = workspace.GetPath("SampleLib", "Cat.cs");
        var firstBefore = await File.ReadAllTextAsync(first);
        var secondBefore = await File.ReadAllTextAsync(second);
        const string skippedTokens = "#if false\n#pragma warning banana junk\n#endif\n";
        var parsed = CSharpSyntaxTree.ParseText(skippedTokens + secondBefore);
        Assert.IsEmpty(parsed.GetDiagnostics(), "The fixture must reach the skipped-token fallback.");
        Assert.IsTrue(parsed.GetRoot().DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia => trivia.IsKind(SyntaxKind.SkippedTokensTrivia)));
        var server = await GetPathAuthorizedServerAsync();
        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            MultiFileEditTools.PreviewMultiFileEdit(server, WorkspaceExecutionGate, EditService, workspace.WorkspaceId,
                [new(first, [new(1, 1, 1, 1, "// valid edit\n")]), new(second, [new(1, 1, 1, 1, skippedTokens)])]));
        StringAssert.Contains(ex.Message, "produce 1 C# syntax error(s)");
        const string tool = "preview_multi_file_edit";
        var envelopeJson = ToolErrorHandler.FormatErrorResponse(ToolErrorHandler.ClassifyError(ex, tool), tool, ex);
        using var envelope = JsonDocument.Parse(envelopeJson);
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", envelope.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(envelope.RootElement.GetProperty("message").GetString()!, "Correct the C# syntax");
        Assert.IsFalse(envelopeJson.Contains("Cat.cs", StringComparison.Ordinal));
        Assert.IsFalse(envelopeJson.Contains("banana", StringComparison.Ordinal));
        Assert.AreEqual(firstBefore, await File.ReadAllTextAsync(first));
        Assert.AreEqual(secondBefore, await File.ReadAllTextAsync(second));
    }

    [TestMethod]
    public async Task PreviewMultiFile_F17NamespaceCommentBrace_ThrowsWhenSyntaxCheckEnabled()
    {
        var edit = new EditService(
            WorkspaceManager,
            NullLogger<EditService>.Instance,
            UndoService,
            ChangeTracker,
            PreviewStore,
            CompileCheckService);
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var canonicalDog = Path.Combine(RepositoryRootPath, "samples", "SampleSolution", "SampleLib", "Dog.cs");
        var firstLine = (await File.ReadAllTextAsync(canonicalDog, CancellationToken.None))
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];
        var colAfterLine1 = firstLine.Length + 1;
        var fileEdits = new[]
        {
            new FileEditsDto(
                dogFilePath,
                new[] { new TextEditDto(1, colAfterLine1, 1, colAfterLine1, "\n// F17\n{\n") }),
        };

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
        {
            await edit.PreviewMultiFileTextEditsAsync(
                workspaceId, fileEdits, CancellationToken.None, skipSyntaxCheck: false);
        });
        StringAssert.Contains(ex.Message, "syntax", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task PreviewMultiFile_F17Shape_SkipSyntaxCheck_StillProducesPreview()
    {
        var edit = new EditService(
            WorkspaceManager,
            NullLogger<EditService>.Instance,
            UndoService,
            ChangeTracker,
            PreviewStore,
            CompileCheckService);
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var canonicalDog = Path.Combine(RepositoryRootPath, "samples", "SampleSolution", "SampleLib", "Dog.cs");
        var firstLine = (await File.ReadAllTextAsync(canonicalDog, CancellationToken.None))
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];
        var colAfterLine1 = firstLine.Length + 1;
        var fileEdits = new[]
        {
            new FileEditsDto(
                dogFilePath,
                new[] { new TextEditDto(1, colAfterLine1, 1, colAfterLine1, "\n// F17\n{\n") }),
        };

        var dto = await edit.PreviewMultiFileTextEditsAsync(
            workspaceId, fileEdits, CancellationToken.None, skipSyntaxCheck: true);

        Assert.IsFalse(string.IsNullOrEmpty(dto.PreviewToken));
        Assert.IsTrue(dto.Changes?.Count > 0);
    }

    // ------------------------------------------------------------------
    // edit-preview-validation-decomposition: both preview entry points now share one
    // SimulatePreviewAsync body. These cases pin the shared body's two observable behaviours —
    // syntax preflight and cross-file diff ordering — from BOTH call sites, so a future change to
    // the shared helper cannot silently diverge the public and internal surfaces.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task PreviewMultiFileOnSolution_F17NamespaceCommentBrace_ThrowsWhenSyntaxCheckEnabled()
    {
        var edit = NewEditService();
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var fileEdits = new[] { new FileEditsDto(dogFilePath, new[] { await F17EditAsync(dogFilePath) }) };
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
        {
            await edit.PreviewMultiFileTextEditsOnSolutionAsync(
                solution, fileEdits, CancellationToken.None, skipSyntaxCheck: false);
        });
        StringAssert.Contains(ex.Message, "Correct the C# syntax and reissue the preview.");
        Assert.IsFalse(ex.Message.Contains("skipSyntaxCheck", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PreviewMultiFile_BothEntryPoints_ProduceIdenticalCrossFileDiffOrdering()
    {
        var edit = NewEditService();
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Cat first, Dog second — the shared helper must emit diffs in the caller's input order,
        // not in path or document order.
        var fileEdits = new[]
        {
            new FileEditsDto(catFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// cat marker\n") }),
            new FileEditsDto(dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// dog marker\n") }),
        };

        var viaSolution = await edit.PreviewMultiFileTextEditsOnSolutionAsync(
            WorkspaceManager.GetCurrentSolution(workspaceId), fileEdits, CancellationToken.None);
        var viaWorkspace = await edit.PreviewMultiFileTextEditsAsync(
            workspaceId, fileEdits, CancellationToken.None);

        CollectionAssert.AreEqual(
            viaSolution.Changes.Select(c => c.FilePath).ToArray(),
            viaWorkspace.Changes!.Select(c => c.FilePath).ToArray(),
            "Both preview entry points must report the same files in the same order.");
        CollectionAssert.AreEqual(
            viaSolution.Changes.Select(c => c.UnifiedDiff).ToArray(),
            viaWorkspace.Changes!.Select(c => c.UnifiedDiff).ToArray(),
            "Both preview entry points must produce byte-identical unified diffs.");
        Assert.AreEqual(catFilePath, viaWorkspace.Changes![0].FilePath, "Input order must be preserved.");
        Assert.AreEqual(viaSolution.Description, viaWorkspace.Description);
    }

    [TestMethod]
    public async Task WhitespaceOnlyEdit_PreviewHasHunk_AndApplyWritesExactText()
    {
        var edit = NewEditService();
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Dog.cs");
        var before = await File.ReadAllTextAsync(filePath, CancellationToken.None);
        var edits = new[]
        {
            new FileEditsDto(filePath, new[] { new TextEditDto(1, 1, 1, 1, " ") }),
        };

        var preview = await edit.PreviewMultiFileTextEditsAsync(workspace.WorkspaceId, edits, CancellationToken.None);
        var diff = preview.Changes.Single().UnifiedDiff;
        StringAssert.Contains(diff, "@@");
        StringAssert.Contains(diff, $"-{before.Split('\n')[0].TrimEnd('\r')}");
        StringAssert.Contains(diff, $"+ {before.Split('\n')[0].TrimEnd('\r')}");

        var applied = await edit.ApplyMultiFileTextEditsAsync(
            workspace.WorkspaceId, edits, "apply_multi_file_edit", CancellationToken.None);
        Assert.IsTrue(applied.Success);
        Assert.AreEqual(" " + before, await File.ReadAllTextAsync(filePath, CancellationToken.None));
    }

    private static EditService NewEditService() => new(
        WorkspaceManager,
        NullLogger<EditService>.Instance,
        UndoService,
        ChangeTracker,
        PreviewStore,
        CompileCheckService);

    /// <summary>
    /// The Jellyfin F17 parser-recovery shape (file-scoped namespace + line comment + stray brace)
    /// appended to the end of line 1 of <paramref name="filePath"/>.
    /// </summary>
    private static async Task<TextEditDto> F17EditAsync(string filePath)
    {
        var firstLine = (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];
        var colAfterLine1 = firstLine.Length + 1;
        return new TextEditDto(1, colAfterLine1, 1, colAfterLine1, "\n// F17\n{\n");
    }
}
