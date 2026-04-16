using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringToolsIntegrationTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task OrganizeUsings_Preview_Returns_Json()
    {
        var filePath = FindDocumentPath("Program.cs");
        var json = await RefactoringTools.PreviewOrganizeUsings(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            filePath,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("previewToken", out _));
    }

    [TestMethod]
    public async Task FormatDocument_Preview_Returns_PreviewToken()
    {
        var filePath = FindDocumentPath("Program.cs");
        var json = await RefactoringTools.PreviewFormatDocument(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            filePath,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("previewToken", out _));
    }

    [TestMethod]
    public async Task Rename_Preview_On_Method_Returns_PreviewToken()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var json = await RefactoringTools.PreviewRename(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            newName: "GetAllAnimalsRenamed",
            filePath,
            line: 7,
            column: 26,
            symbolHandle: null,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("previewToken", out _));
    }

    [TestMethod]
    public async Task RenamePreview_RejectsNumericPrefix()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var locator = SymbolLocator.BySource(filePath, 7, 26);
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            RefactoringService.PreviewRenameAsync(WorkspaceId, locator, "2foo", CancellationToken.None));
        StringAssert.Contains(ex.Message, "is not a valid C# identifier");
    }

    [TestMethod]
    public async Task RenamePreview_RejectsReservedKeyword()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var locator = SymbolLocator.BySource(filePath, 7, 26);
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            RefactoringService.PreviewRenameAsync(WorkspaceId, locator, "class", CancellationToken.None));
        StringAssert.Contains(ex.Message, "reserved C# keyword");
    }

    [TestMethod]
    public async Task RenamePreview_RejectsContextualKeyword()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var locator = SymbolLocator.BySource(filePath, 7, 26);
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            RefactoringService.PreviewRenameAsync(WorkspaceId, locator, "var", CancellationToken.None));
        StringAssert.Contains(ex.Message, "contextual C# keyword");
    }

    [TestMethod]
    public async Task RenamePreview_AcceptsVerbatimKeyword()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var locator = SymbolLocator.BySource(filePath, 7, 26);
        var preview = await RefactoringService.PreviewRenameAsync(
            WorkspaceId, locator, "@class", CancellationToken.None);
        Assert.IsNotNull(preview);
        Assert.IsFalse(string.IsNullOrEmpty(preview.PreviewToken));
    }

    [TestMethod]
    public async Task RenamePreview_EmptyName_Rejected()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var locator = SymbolLocator.BySource(filePath, 7, 26);
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            RefactoringService.PreviewRenameAsync(WorkspaceId, locator, string.Empty, CancellationToken.None));
        StringAssert.Contains(ex.Message, "is required");
    }

    [TestMethod]
    public async Task RenamePreview_NoOp_ProducesWarning()
    {
        var filePath = FindDocumentPath("AnimalService.cs");
        var locator = SymbolLocator.BySource(filePath, 7, 26);
        var preview = await RefactoringService.PreviewRenameAsync(
            WorkspaceId, locator, "GetAllAnimals", CancellationToken.None);

        Assert.IsNotNull(preview.Warnings, "Expected Warnings to be populated for a no-op rename.");
        Assert.AreEqual(1, preview.Warnings!.Count);
        StringAssert.Contains(preview.Warnings![0], "matches the existing name");
    }

    [TestMethod]
    public async Task OrganizeUsings_RemovesUnusedUsings_DoesNotLeaveBlankLines()
    {
        // Build an isolated workspace, seed a file with multiple unused usings,
        // organize, apply, then assert the resulting file has no stray blank lines.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var probeFilePath = Path.Combine(copiedRoot, "SampleLib", "TriviaProbe.cs");
            var seeded = string.Join('\n',
                "using System;",
                "using System.IO;",
                "using System.Text;",
                "using System.Linq;",
                "",
                "namespace SampleLib;",
                "",
                "public sealed class TriviaProbe",
                "{",
                "    public int Count => 0;",
                "}",
                "");
            await File.WriteAllTextAsync(probeFilePath, seeded, CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var wsId = status.WorkspaceId;

            var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
                wsId, probeFilePath, CancellationToken.None);
            Assert.IsNotNull(preview.PreviewToken);

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);

            var resultText = await File.ReadAllTextAsync(probeFilePath, CancellationToken.None);
            var normalized = resultText.Replace("\r\n", "\n");

            Assert.IsFalse(normalized.StartsWith('\n'),
                "Resulting file must not begin with a blank line.");
            Assert.IsFalse(normalized.Contains("\n\n\n", StringComparison.Ordinal),
                "Resulting file must not contain runs of 2+ blank lines (three consecutive newlines).");

            WorkspaceManager.Close(wsId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(document.Name, name, StringComparison.Ordinal))?.FilePath;

        return path ?? throw new AssertFailedException($"Document '{name}' was not found.");
    }
}
