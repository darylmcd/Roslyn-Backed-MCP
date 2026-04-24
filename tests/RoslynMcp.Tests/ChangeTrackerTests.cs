using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies that the ChangeTracker records mutations applied through
/// RefactoringService, EditService, and EditorConfigService apply paths —
/// and that each writer surfaces with its originating MCP tool name rather
/// than collapsing into a generic bucket
/// (<c>workspace-changes-log-missing-editorconfig-writers</c>).
/// </summary>
[TestClass]
public sealed class ChangeTrackerTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static SuppressionService CreateSuppressionService() =>
        new SuppressionService(EditorConfigService, EditService, WorkspaceManager, CompileCheckService);

    [TestMethod]
    public async Task RenameApply_RecordsRenameToolName()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");

        var locator = SymbolLocator.BySource(doc.FilePath!, 5, 6);
        var preview = await RefactoringService.PreviewRenameAsync(
            wsId, locator, "Doggo", CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "rename_apply", CancellationToken.None);

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count, "Expected exactly 1 change after rename apply.");
        Assert.AreEqual("rename_apply", changes[0].ToolName,
            "Rename apply must surface as 'rename_apply' rather than the legacy 'refactoring_apply' bucket.");
        Assert.IsTrue(changes[0].AffectedFiles.Count > 0, "Rename should affect at least one file.");
    }

    [TestMethod]
    public async Task FormatDocumentApply_RecordsFormatToolName()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");

        var preview = await RefactoringService.PreviewFormatDocumentAsync(
            wsId, doc.FilePath!, CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "format_document_apply", CancellationToken.None);

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual("format_document_apply", changes[0].ToolName,
            "format_document_apply must not collapse into 'refactoring_apply'.");
    }

    [TestMethod]
    public async Task ApplyTextEdit_RecordsApplyTextEditToolName()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");

        var edit = new TextEditDto(1, 1, 1, 1, "// leading comment\n");
        var result = await EditService.ApplyTextEditsAsync(
            wsId, doc.FilePath!, [edit], "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success, "Text edit should apply.");

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual("apply_text_edit", changes[0].ToolName);
    }

    [TestMethod]
    public async Task SetEditorConfigOption_RecordsEditorConfigToolName()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var setResult = await EditorConfigService.SetOptionAsync(
            wsId, dogFilePath, "dotnet_diagnostic.CA1702.severity", "warning",
            "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(setResult.EditorConfigPath));

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count,
            "set_editorconfig_option must produce a workspace_changes entry — " +
            "previously the editorconfig writer silently bypassed ChangeTracker.");
        Assert.AreEqual("set_editorconfig_option", changes[0].ToolName);
        CollectionAssert.Contains(
            changes[0].AffectedFiles.ToList(),
            setResult.EditorConfigPath,
            "The recorded affected-file set must include the written .editorconfig path.");
    }

    [TestMethod]
    public async Task SetDiagnosticSeverity_RecordsSeverityToolName()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var result = await CreateSuppressionService().SetDiagnosticSeverityAsync(
            wsId, "CA1703", "warning", dogFilePath, CancellationToken.None);
        Assert.IsTrue(File.Exists(result.EditorConfigPath));

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count,
            "set_diagnostic_severity must produce a workspace_changes entry; " +
            "it previously shared the editorconfig-silent-writer bug.");
        Assert.AreEqual("set_diagnostic_severity", changes[0].ToolName,
            "The originating tool name must be 'set_diagnostic_severity' rather than " +
            "the underlying 'set_editorconfig_option' helper it delegates to.");
    }

    [TestMethod]
    public async Task DistinctWriters_ProduceDistinctToolNames()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");
        var dogPath = doc.FilePath!;

        // Writer 1: rename_apply (RefactoringService)
        var locator = SymbolLocator.BySource(dogPath, 5, 6);
        var renamePreview = await RefactoringService.PreviewRenameAsync(
            wsId, locator, "Doggo", CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(
            renamePreview.PreviewToken, "rename_apply", CancellationToken.None);

        // After the rename the file lives at Doggo.cs on disk, so fetch the current
        // path for the next apply from the workspace.
        var currentDogPath = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Dog.cs") == true || d.FilePath?.EndsWith("Doggo.cs") == true)
            .FilePath!;

        // Writer 2: apply_text_edit (EditService)
        var edit = new TextEditDto(1, 1, 1, 1, "// comment\n");
        await EditService.ApplyTextEditsAsync(
            wsId, currentDogPath, [edit], "apply_text_edit", CancellationToken.None);

        // Writer 3: set_editorconfig_option (EditorConfigService)
        await EditorConfigService.SetOptionAsync(
            wsId, currentDogPath, "dotnet_diagnostic.CA1704.severity", "warning",
            "set_editorconfig_option", CancellationToken.None);

        // Writer 4: set_diagnostic_severity (SuppressionService → EditorConfigService)
        await CreateSuppressionService().SetDiagnosticSeverityAsync(
            wsId, "CA1705", "suggestion", currentDogPath, CancellationToken.None);

        var changes = ChangeTracker.GetChanges(wsId);
        var toolNames = changes.Select(c => c.ToolName).ToList();

        CollectionAssert.Contains(toolNames, "rename_apply");
        CollectionAssert.Contains(toolNames, "apply_text_edit");
        CollectionAssert.Contains(toolNames, "set_editorconfig_option");
        CollectionAssert.Contains(toolNames, "set_diagnostic_severity");

        // No writer may collapse into the legacy generic buckets.
        CollectionAssert.DoesNotContain(toolNames, "refactoring_apply",
            "rename_apply must not degrade to the generic 'refactoring_apply' label.");
    }

    [TestMethod]
    public async Task TwoApplies_OrderedBySequence()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");

        // First apply: rename
        var locator = SymbolLocator.BySource(doc.FilePath!, 5, 6);
        var preview1 = await RefactoringService.PreviewRenameAsync(
            wsId, locator, "Doggo", CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview1.PreviewToken, "rename_apply", CancellationToken.None);

        // Second apply: format
        var doc2 = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Dog.cs") == true || d.FilePath?.EndsWith("Doggo.cs") == true);

        var preview2 = await RefactoringService.PreviewFormatDocumentAsync(
            wsId, doc2.FilePath!, CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview2.PreviewToken, "format_document_apply", CancellationToken.None);

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(2, changes.Count, "Expected 2 changes.");
        Assert.IsTrue(changes[0].SequenceNumber < changes[1].SequenceNumber,
            "Changes should be ordered by sequence number.");
    }

    [TestMethod]
    public async Task ClearWorkspace_RemovesChanges()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.RecordChange(wsId, "test", ["file.cs"], "test_tool");
        Assert.AreEqual(1, ChangeTracker.GetChanges(wsId).Count);

        ChangeTracker.Clear(wsId);
        Assert.AreEqual(0, ChangeTracker.GetChanges(wsId).Count);
    }

    [TestMethod]
    public void EmptyWorkspace_ReturnsEmptyList()
    {
        var changes = ChangeTracker.GetChanges("nonexistent-workspace-id");
        Assert.AreEqual(0, changes.Count);
    }
}
