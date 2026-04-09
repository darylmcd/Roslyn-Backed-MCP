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
        var (document, sourceText) = await ResolveDocumentAndTextAsync(solution, filePath, ct).ConfigureAwait(false);

        // apply-text-edit-invalid-edit-corrupt-diff: validate ranges up front so bad
        // edits throw a structured ArgumentException BEFORE we touch the file or the
        // unified diff. Without this, a reversed/out-of-bounds range could produce a
        // corrupt diff (ITChatBot audit I1, 2026-04-08).
        ValidateEdits(filePath, edits, sourceText);

        // Capture pre-apply snapshot so revert_last_apply can roll back this edit. We
        // pass BOTH the solution (for the legacy path) AND an explicit file snapshot
        // (for the authoritative file-based restore path — see FLAG-9A in UndoService).
        var fileSnapshots = new[]
        {
            new FileSnapshotDto(Path.GetFullPath(filePath), sourceText.ToString()),
        };
        _undoService?.CaptureBeforeApply(
            workspaceId,
            $"Apply text edit to {Path.GetFileName(filePath)}",
            solution,
            fileSnapshots);

        return await ApplyTextEditsCoreAsync(workspaceId, filePath, edits, solution, document, sourceText, ct).ConfigureAwait(false);
    }

    public async Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct)
    {
        var initialSolution = _workspace.GetCurrentSolution(workspaceId);

        // apply-text-edit-invalid-edit-corrupt-diff: validate every file's edits BEFORE
        // we take the undo snapshot and BEFORE any disk write. The whole batch fails
        // fast if any single edit is malformed, so the caller never sees a half-applied
        // state or a corrupt diff on the surviving files.
        var perFileSnapshots = new List<(Document Document, SourceText SourceText, string NormalizedPath)>();
        foreach (var fileEdit in fileEdits)
        {
            var (document, sourceText) = await ResolveDocumentAndTextAsync(initialSolution, fileEdit.FilePath, ct).ConfigureAwait(false);
            ValidateEdits(fileEdit.FilePath, fileEdit.Edits, sourceText);
            perFileSnapshots.Add((document, sourceText, Path.GetFullPath(fileEdit.FilePath)));
        }

        // Single snapshot at the top so revert_last_apply rolls back the ENTIRE batch atomically
        // (from an undo perspective; individual disk writes still happen sequentially).
        var fileSnapshots = perFileSnapshots
            .Select(t => new FileSnapshotDto(t.NormalizedPath, t.SourceText.ToString()))
            .ToList();
        _undoService?.CaptureBeforeApply(
            workspaceId,
            $"Apply edits to {fileEdits.Count} file(s)",
            initialSolution,
            fileSnapshots);

        var results = new List<FileEditSummaryDto>();
        foreach (var fileEdit in fileEdits)
        {
            // Each per-file apply uses the FRESH current solution because the previous edit
            // is now committed. Pass it explicitly so the core path doesn't take its own
            // (redundant) snapshot.
            var current = _workspace.GetCurrentSolution(workspaceId);
            var (document, sourceText) = await ResolveDocumentAndTextAsync(current, fileEdit.FilePath, ct).ConfigureAwait(false);
            var result = await ApplyTextEditsCoreAsync(
                workspaceId, fileEdit.FilePath, fileEdit.Edits, current, document, sourceText, ct).ConfigureAwait(false);
            var diff = result.Changes.Count > 0
                ? string.Join("\n", result.Changes.Select(ch => ch.UnifiedDiff))
                : null;
            results.Add(new FileEditSummaryDto(fileEdit.FilePath, result.EditsApplied, diff));
        }

        return new MultiFileEditResultDto(true, results.Count, results);
    }

    private static async Task<(Document Document, SourceText SourceText)> ResolveDocumentAndTextAsync(
        Solution solution,
        string filePath,
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
        return (document, sourceText);
    }

    /// <summary>
    /// Rejects malformed <see cref="TextEditDto"/> values before the edit ever touches the
    /// document. The ITChatBot deep-review audit (2026-04-08 incident I1,
    /// <c>apply-text-edit-invalid-edit-corrupt-diff</c>) showed that an invalid / zero-width
    /// range could reach the DiffPlex path and produce a corrupt unified diff. The checks
    /// here short-circuit BEFORE any workspace mutation so the tool surfaces a structured
    /// <see cref="ArgumentException"/> via <c>ToolErrorHandler</c>.
    /// </summary>
    private static void ValidateEdits(
        string filePath,
        IReadOnlyList<TextEditDto> edits,
        SourceText sourceText)
    {
        if (edits.Count == 0)
        {
            throw new ArgumentException($"At least one text edit is required for '{filePath}'.", nameof(edits));
        }

        var lineCount = sourceText.Lines.Count;

        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];

            if (edit.NewText is null)
            {
                throw new ArgumentException(
                    $"Edit #{i} for '{filePath}' has a null NewText. Use an empty string for deletions.",
                    nameof(edits));
            }

            if (edit.StartLine < 1 || edit.StartColumn < 1 || edit.EndLine < 1 || edit.EndColumn < 1)
            {
                throw new ArgumentException(
                    $"Edit #{i} for '{filePath}' has non-positive line/column: " +
                    $"({edit.StartLine},{edit.StartColumn})-({edit.EndLine},{edit.EndColumn}). " +
                    "Line and column are 1-based.",
                    nameof(edits));
            }

            if (edit.StartLine > lineCount || edit.EndLine > lineCount)
            {
                throw new ArgumentException(
                    $"Edit #{i} for '{filePath}' references line {Math.Max(edit.StartLine, edit.EndLine)} " +
                    $"but the file only has {lineCount} line(s).",
                    nameof(edits));
            }

            var startLineLength = sourceText.Lines[edit.StartLine - 1].SpanIncludingLineBreak.Length;
            if (edit.StartColumn > startLineLength + 1)
            {
                throw new ArgumentException(
                    $"Edit #{i} for '{filePath}' has StartColumn {edit.StartColumn} but line {edit.StartLine} " +
                    $"only has {startLineLength} character(s). Columns are 1-based and may be one past the end.",
                    nameof(edits));
            }

            var endLineLength = sourceText.Lines[edit.EndLine - 1].SpanIncludingLineBreak.Length;
            if (edit.EndColumn > endLineLength + 1)
            {
                throw new ArgumentException(
                    $"Edit #{i} for '{filePath}' has EndColumn {edit.EndColumn} but line {edit.EndLine} " +
                    $"only has {endLineLength} character(s).",
                    nameof(edits));
            }

            if (edit.StartLine > edit.EndLine
                || (edit.StartLine == edit.EndLine && edit.StartColumn > edit.EndColumn))
            {
                throw new ArgumentException(
                    $"Edit #{i} for '{filePath}' has a reversed range: " +
                    $"start ({edit.StartLine},{edit.StartColumn}) is after end ({edit.EndLine},{edit.EndColumn}). " +
                    "Zero-width ranges are allowed (inserts) but the end position must not precede the start.",
                    nameof(edits));
            }
        }
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
        Document document,
        SourceText sourceText,
        CancellationToken ct)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var originalText = sourceText.ToString();

        // Sort edits in reverse order to apply from bottom to top (so offsets remain valid)
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

        var textChanges = new List<TextChange>();
        foreach (var edit in sortedEdits)
        {
            var startPosition = sourceText.Lines.GetPosition(new LinePosition(edit.StartLine - 1, edit.StartColumn - 1));
            var endPosition = sourceText.Lines.GetPosition(new LinePosition(edit.EndLine - 1, edit.EndColumn - 1));
            var span = TextSpan.FromBounds(startPosition, endPosition);

            // dr-apply-text-edit-line-break-corruption: When the edit span ends at
            // column 1 of a line (meaning it swallowed the line break of the previous
            // line), and NewText does not end with a line break, append the original
            // line ending to prevent line collapse at method/declaration boundaries.
            var replacementText = edit.NewText;
            if (edit.EndColumn == 1 && span.Length > 0 && replacementText.Length > 0)
            {
                var lastCharInSpan = sourceText[span.End - 1];
                var endsWithNewline = replacementText[^1] is '\n' or '\r';
                if (lastCharInSpan is '\n' or '\r' && !endsWithNewline)
                {
                    // Detect the original line ending sequence (CRLF vs LF vs CR)
                    var lineBreak = (span.End >= 2 && sourceText[span.End - 2] == '\r' && lastCharInSpan == '\n')
                        ? "\r\n"
                        : lastCharInSpan == '\n' ? "\n" : "\r";
                    replacementText += lineBreak;
                }
            }

            textChanges.Add(new TextChange(span, replacementText));
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
