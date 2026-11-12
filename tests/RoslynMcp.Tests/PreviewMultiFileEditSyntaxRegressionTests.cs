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
    [DataRow("preview", "exact")]
    [DataRow("preview", "alternate")]
    [DataRow("preview", "link")]
    [DataRow("solution", "alternate")]
    [DataRow("solution", "link")]
    [DataRow("direct", "alternate")]
    [DataRow("direct", "link")]
    public async Task MultiFileEdit_RejectsRepeatedResolvedTargetBeforePublishing(string route, string aliasKind)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var workspaceId = workspace.WorkspaceId;
        var target = workspace.GetPath("SampleLib", "Dog.cs");
        var other = workspace.GetPath("SampleLib", "Cat.cs");
        var targetBefore = await File.ReadAllTextAsync(target);
        var otherBefore = await File.ReadAllTextAsync(other);
        var alias = aliasKind == "exact" ? target : workspace.GetPath("SampleLib", ".", "Dog.cs");
        if (aliasKind == "link")
        {
            var link = workspace.GetPath("LinkedSampleLib");
            Assert.IsTrue(TestFixtureFileSystem.TryCreateDirectoryLink(link, workspace.GetPath("SampleLib")));
            alias = Path.Combine(link, "Dog.cs");
        }

        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var store = new PreviewStore(maxEntries: 1);
        var existingToken = store.Store(workspaceId, solution, WorkspaceManager.GetCurrentVersion(workspaceId), "Prior preview");
        var edit = new EditService(WorkspaceManager, NullLogger<EditService>.Instance,
            UndoService, ChangeTracker, store, CompileCheckService);
        UndoService.CaptureBeforeApply(workspaceId, "Prior operation", solution, fileSnapshots: null);
        var priorUndo = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(priorUndo);
        var server = await GetPathAuthorizedServerAsync();
        FileEditsDto[] fileEdits =
        [
            new(other, [new(1, 1, 1, 1, "// earlier target\n")]),
            new(target, [new(1, 1, 1, 1, "// first edit\n")]),
            new(alias, [new(2, 1, 2, 1, "// second edit\n")]),
        ];

        var exception = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => route switch
        {
            "preview" => MultiFileEditTools.PreviewMultiFileEdit(server, WorkspaceExecutionGate, edit, workspaceId, fileEdits),
            "solution" => edit.PreviewMultiFileTextEditsOnSolutionAsync(solution, fileEdits, CancellationToken.None),
            "direct" => MultiFileEditTools.ApplyMultiFileEdit(server, WorkspaceExecutionGate, edit, workspaceId, fileEdits),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        });

        var tool = route == "direct" ? "apply_multi_file_edit" : "preview_multi_file_edit";
        var envelopeJson = ToolErrorHandler.FormatErrorResponse(ToolErrorHandler.ClassifyError(exception, tool), tool, exception);
        using var envelope = JsonDocument.Parse(envelopeJson);
        Assert.AreEqual("InvalidArgument", envelope.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(envelope.RootElement.GetProperty("message").GetString()!, "Combine edits for each file");
        Assert.IsFalse(envelope.RootElement.TryGetProperty("previewToken", out _));
        Assert.AreEqual(workspaceId, store.PeekWorkspaceId(existingToken), "Refusal must not mint a token that evicts the prior one.");
        Assert.AreEqual(priorUndo, UndoService.GetLastOperation(workspaceId), "Refusal must preserve the prior undo target.");
        Assert.AreEqual(targetBefore, await File.ReadAllTextAsync(target));
        Assert.AreEqual(otherBefore, await File.ReadAllTextAsync(other));
    }

    [TestMethod]
    public async Task DirectMultiFileEdit_RejectsTargetRedirectedAfterPreflight()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var one = workspace.GetPath("SampleLib", "One", "Target.cs");
        var two = workspace.GetPath("SampleLib", "Two", "Target.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(one)!);
        Directory.CreateDirectory(Path.GetDirectoryName(two)!);
        await File.WriteAllTextAsync(one, "public class FirstTarget {}\n");
        await File.WriteAllTextAsync(two, "public class SecondTarget {}\n");
        await workspace.LoadAsync();

        var workspaceId = workspace.WorkspaceId;
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var paths = solution.Projects.SelectMany(project => project.Documents)
            .Select(document => document.FilePath)
            .Where(path => path == one || path == two)
            .ToArray();
        Assert.HasCount(2, paths);
        var first = paths[0]!;
        var second = paths[1]!;
        var firstBefore = await File.ReadAllTextAsync(first);
        var secondBefore = await File.ReadAllTextAsync(second);
        var secondDirectory = Path.GetDirectoryName(second)!;
        var displacedDirectory = workspace.GetPath("DisplacedTarget");
        var compileCheck = new AfterCompileCheck(CompileCheckService, () =>
        {
            if (Directory.Exists(displacedDirectory)) return;
            Directory.Move(secondDirectory, displacedDirectory);
            Assert.IsTrue(TestFixtureFileSystem.TryCreateDirectoryLink(secondDirectory, Path.GetDirectoryName(first)!));
        });
        var edit = new EditService(WorkspaceManager, NullLogger<EditService>.Instance,
            UndoService, ChangeTracker, PreviewStore, compileCheck);
        UndoService.CaptureBeforeApply(workspaceId, "Prior operation", solution, fileSnapshots: null);
        var priorUndo = UndoService.GetLastOperation(workspaceId);
        var server = await GetPathAuthorizedServerAsync();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            MultiFileEditTools.ApplyMultiFileEdit(server, WorkspaceExecutionGate, edit, workspaceId,
                [new(first, [new(1, 1, 1, 1, "// first\n")]), new(second, [new(1, 1, 1, 1, "// second\n")])], verify: true));
        StringAssert.Contains(exception.Message, "filesystem-link component");
        Assert.AreEqual(firstBefore, await File.ReadAllTextAsync(first));
        Assert.AreEqual(secondBefore, await File.ReadAllTextAsync(Path.Combine(displacedDirectory, "Target.cs")));
        Assert.AreEqual(priorUndo, UndoService.GetLastOperation(workspaceId));
    }

    private sealed class AfterCompileCheck(ICompileCheckService inner, Action afterCheck) : ICompileCheckService
    {
        public async Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct)
        {
            var result = await inner.CheckAsync(workspaceId, options, ct);
            afterCheck();
            return result;
        }
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
