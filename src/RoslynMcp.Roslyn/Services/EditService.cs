using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class EditService : IEditService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<EditService> _logger;
    private readonly IUndoService? _undoService;

    public EditService(IWorkspaceManager workspace, ILogger<EditService> logger, IUndoService? undoService = null)
    {
        _workspace = workspace;
        _logger = logger;
        _undoService = undoService;
    }

    public async Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Capture pre-apply snapshot so revert_last_apply can roll back this edit. UndoService
        // is single-slot per workspace; a stale snapshot from a failed apply is harmless and
        // gets overwritten by the next successful apply.
        _undoService?.CaptureBeforeApply(
            workspaceId,
            $"Apply text edit to {Path.GetFileName(filePath)}",
            solution);

        return await ApplyTextEditsCoreAsync(workspaceId, filePath, edits, solution, ct).ConfigureAwait(false);
    }

    public async Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct)
    {
        // Single snapshot at the top so revert_last_apply rolls back the ENTIRE batch atomically
        // (from an undo perspective; individual disk writes still happen sequentially).
        var initialSolution = _workspace.GetCurrentSolution(workspaceId);
        _undoService?.CaptureBeforeApply(
            workspaceId,
            $"Apply edits to {fileEdits.Count} file(s)",
            initialSolution);

        var results = new List<FileEditSummaryDto>();
        foreach (var fileEdit in fileEdits)
        {
            // Each per-file apply uses the FRESH current solution because the previous edit
            // is now committed. Pass it explicitly so the core path doesn't take its own
            // (redundant) snapshot.
            var current = _workspace.GetCurrentSolution(workspaceId);
            var result = await ApplyTextEditsCoreAsync(
                workspaceId, fileEdit.FilePath, fileEdit.Edits, current, ct).ConfigureAwait(false);
            var diff = result.Changes.Count > 0
                ? string.Join("\n", result.Changes.Select(ch => ch.UnifiedDiff))
                : null;
            results.Add(new FileEditSummaryDto(fileEdit.FilePath, result.EditsApplied, diff));
        }

        return new MultiFileEditResultDto(true, results.Count, results);
    }

    /// <summary>
    /// Inner edit-application path that does NOT touch the undo stack. The caller is
    /// responsible for snapshotting before invoking this method (single-file path
    /// snapshots once; multi-file path snapshots once at the batch boundary).
    /// </summary>
    private async Task<TextEditResultDto> ApplyTextEditsCoreAsync(
        string workspaceId,
        string filePath,
        IReadOnlyList<TextEditDto> edits,
        Solution solution,
        CancellationToken ct)
    {
        var normalizedPath = Path.GetFullPath(filePath);

        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null &&
                Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (document is null)
            throw new KeyNotFoundException($"Document not found in workspace: {filePath}");

        var sourceText = await document.GetTextAsync(ct).ConfigureAwait(false);
        var originalText = sourceText.ToString();

        // Sort edits in reverse order to apply from bottom to top (so offsets remain valid)
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

        var textChanges = new List<TextChange>();
        foreach (var edit in sortedEdits)
        {
            var startPosition = sourceText.Lines.GetPosition(new LinePosition(edit.StartLine - 1, edit.StartColumn - 1));
            var endPosition = sourceText.Lines.GetPosition(new LinePosition(edit.EndLine - 1, edit.EndColumn - 1));
            var span = TextSpan.FromBounds(startPosition, endPosition);
            textChanges.Add(new TextChange(span, edit.NewText));
        }

        var newSourceText = sourceText.WithChanges(textChanges);
        var newDocument = document.WithText(newSourceText);
        var newSolution = newDocument.Project.Solution;

        var applied = _workspace.TryApplyChanges(workspaceId, newSolution);
        if (!applied)
        {
            return new TextEditResultDto(false, filePath, 0, []);
        }

        // BUG-N1: Mirror RefactoringService — MSBuildWorkspace may not flush text edits to disk.
        var persisted = await PersistDocumentTextToDiskAsync(workspaceId, normalizedPath, ct).ConfigureAwait(false);
        if (!persisted)
        {
            return new TextEditResultDto(false, filePath, 0, []);
        }

        // Compute diff
        var newText = newSourceText.ToString();
        var differ = new Differ();
        var diffResult = InlineDiffBuilder.Diff(originalText, newText);

        var diffLines = new List<string>();
        foreach (var line in diffResult.Lines)
        {
            var prefix = line.Type switch
            {
                ChangeType.Inserted => "+ ",
                ChangeType.Deleted => "- ",
                _ => "  "
            };
            diffLines.Add(prefix + line.Text);
        }

        var fileChange = new FileChangeDto(filePath, string.Join('\n', diffLines));

        return new TextEditResultDto(true, filePath, edits.Count, [fileChange]);
    }

    private async Task<bool> PersistDocumentTextToDiskAsync(string workspaceId, string normalizedPath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null &&
                Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (document?.FilePath is null)
            return false;

        try
        {
            var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to persist document to disk: {Path}", document.FilePath);
            return false;
        }
    }
}
