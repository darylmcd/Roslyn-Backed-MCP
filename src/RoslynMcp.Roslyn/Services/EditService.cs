using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class EditService : IEditService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<EditService> _logger;
    private readonly IUndoService? _undoService;
    private readonly IChangeTracker? _changeTracker;
    private readonly Contracts.IPreviewStore? _previewStore;

    public EditService(
        IWorkspaceManager workspace,
        ILogger<EditService> logger,
        IUndoService? undoService = null,
        IChangeTracker? changeTracker = null,
        Contracts.IPreviewStore? previewStore = null)
    {
        _workspace = workspace;
        _logger = logger;
        _undoService = undoService;
        _changeTracker = changeTracker;
        _previewStore = previewStore;
    }

    public async Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct, bool skipSyntaxCheck = false)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var (document, sourceText) = await ResolveDocumentAndTextAsync(solution, filePath, ct).ConfigureAwait(false);

        // apply-text-edit-invalid-edit-corrupt-diff: validate ranges up front so bad
        // edits throw a structured ArgumentException BEFORE we touch the file or the
        // unified diff. Without this, a reversed/out-of-bounds range could produce a
        // corrupt diff (ITChatBot audit I1, 2026-04-08).
        ValidateEdits(filePath, edits, sourceText);

        var newSourceText = BuildPatchedSourceText(sourceText, edits);
        if (!skipSyntaxCheck
            && string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            var syntaxErrors = GetCSharpSyntaxErrors(newSourceText, filePath);
            if (syntaxErrors.Count > 0)
            {
                return new TextEditResultDto(false, filePath, 0, [], syntaxErrors);
            }
        }

        // Capture pre-apply snapshot so revert_last_apply can roll back this edit. We
        // pass BOTH the solution (for the legacy path) AND an explicit file snapshot
        // (for the authoritative file-based restore path — see FLAG-9A in UndoService).
        // Syntax check runs before capture so a rejected edit does not leave a no-op undo entry.
        var fileSnapshots = new[]
        {
            new FileSnapshotDto(Path.GetFullPath(filePath), sourceText.ToString()),
        };
        _undoService?.CaptureBeforeApply(
            workspaceId,
            $"Apply text edit to {Path.GetFileName(filePath)}",
            solution,
            fileSnapshots);

        return await ApplyTextEditsCoreAsync(workspaceId, filePath, edits, solution, document, sourceText, newSourceText, ct).ConfigureAwait(false);
    }

    public async Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false)
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
            var merged = BuildPatchedSourceText(sourceText, fileEdit.Edits);
            if (!skipSyntaxCheck
                && string.Equals(Path.GetExtension(fileEdit.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                var syntaxErrors = GetCSharpSyntaxErrors(merged, fileEdit.FilePath);
                if (syntaxErrors.Count > 0)
                {
                    results.Add(new FileEditSummaryDto(fileEdit.FilePath, 0, null));
                    continue;
                }
            }

            var result = await ApplyTextEditsCoreAsync(
                workspaceId, fileEdit.FilePath, fileEdit.Edits, current, document, sourceText, merged, ct).ConfigureAwait(false);
            var diff = result.Changes.Count > 0
                ? string.Join("\n", result.Changes.Select(ch => ch.UnifiedDiff))
                : null;
            results.Add(new FileEditSummaryDto(fileEdit.FilePath, result.EditsApplied, diff));
        }

        return new MultiFileEditResultDto(true, results.Count, results);
    }

    /// <summary>
    /// Item 5: preview a multi-file edit batch without writing to disk. Simulates every file's
    /// edits against a single Roslyn <c>Solution</c> snapshot, stores the mutated snapshot in
    /// <see cref="Contracts.IPreviewStore"/>, and returns per-file unified diffs plus a
    /// composite preview token. Callers redeem via <c>apply_composite_preview</c>.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false)
    {
        if (_previewStore is null)
        {
            throw new InvalidOperationException(
                "preview_multi_file_edit requires IPreviewStore to be registered. Ensure RoslynMcp.Roslyn DI is configured.");
        }
        if (fileEdits is null || fileEdits.Count == 0)
        {
            throw new InvalidOperationException("preview_multi_file_edit requires at least one file edit.");
        }

        var initialSolution = _workspace.GetCurrentSolution(workspaceId);

        // Pre-validate every file's edits BEFORE issuing any preview token. A malformed edit
        // aborts the whole preview so callers see the error without a dangling token.
        var perFile = new List<(Document Document, SourceText SourceText, string FilePath, IReadOnlyList<TextEditDto> Edits)>();
        foreach (var fileEdit in fileEdits)
        {
            var (document, sourceText) = await ResolveDocumentAndTextAsync(initialSolution, fileEdit.FilePath, ct).ConfigureAwait(false);
            ValidateEdits(fileEdit.FilePath, fileEdit.Edits, sourceText);
            perFile.Add((document, sourceText, fileEdit.FilePath, fileEdit.Edits));
        }

        // Simulate edits on a running Solution snapshot so the stored preview matches what
        // apply_composite_preview will redeem.
        var accumulator = initialSolution;
        var changes = new List<FileChangeDto>();
        var warnings = new List<string>();

        foreach (var (document, sourceText, filePath, edits) in perFile)
        {
            var merged = BuildPatchedSourceText(sourceText, edits);
            if (!skipSyntaxCheck
                && string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                var syntaxErrors = GetCSharpSyntaxErrors(merged, filePath);
                if (syntaxErrors.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"preview_multi_file_edit: simulated edits for '{filePath}' produce {syntaxErrors.Count} syntax error(s). " +
                        "Pass skipSyntaxCheck=true if the intermediate state is intentional.");
                }
            }

            // Find the document in the accumulating solution so its updated text flows into the
            // next file's diff comparison.
            var docInAccum = accumulator.GetDocument(document.Id);
            if (docInAccum is null)
            {
                // Document was removed by a prior edit in the batch — skip with a warning.
                warnings.Add($"Skipped '{filePath}': document no longer present in the working solution.");
                continue;
            }
            accumulator = accumulator.WithDocumentText(docInAccum.Id, merged);

            var unified = DiffGenerator.GenerateUnifiedDiff(sourceText.ToString(), merged.ToString(), filePath);
            changes.Add(new FileChangeDto(filePath, unified));
        }

        if (changes.Count == 0)
        {
            throw new InvalidOperationException("preview_multi_file_edit produced no diffs. See Warnings for per-file reasons.");
        }

        var description = $"Preview multi-file edit across {changes.Count} file(s)";
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
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

        ValidateNoOverlappingEdits(filePath, edits, sourceText);
    }

    /// <summary>
    /// apply-text-edit-overlap: Overlapping spans passed to <see cref="SourceText.WithChanges"/>
    /// produce undefined merge behavior. Reject before any mutation.
    /// </summary>
    private static void ValidateNoOverlappingEdits(string filePath, IReadOnlyList<TextEditDto> edits, SourceText sourceText)
    {
        if (edits.Count < 2)
        {
            return;
        }

        var spans = new List<(int Index, TextSpan Span)>(edits.Count);
        for (var i = 0; i < edits.Count; i++)
        {
            spans.Add((i, GetSpanForEdit(edits[i], sourceText)));
        }

        spans.Sort((a, b) =>
        {
            var c = a.Span.Start.CompareTo(b.Span.Start);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < spans.Count - 1; i++)
        {
            var left = spans[i];
            var right = spans[i + 1];
            if (left.Span.Start < right.Span.End && right.Span.Start < left.Span.End)
            {
                var le = edits[left.Index];
                var re = edits[right.Index];
                throw new ArgumentException(
                    $"Edits #{left.Index} and #{right.Index} for '{filePath}' have overlapping spans: " +
                    $"({le.StartLine},{le.StartColumn})-({le.EndLine},{le.EndColumn}) vs " +
                    $"({re.StartLine},{re.StartColumn})-({re.EndLine},{re.EndColumn}). " +
                    "Merge edits into one range or apply them in separate calls.",
                    nameof(edits));
            }
        }
    }

    private static TextSpan GetSpanForEdit(TextEditDto edit, SourceText sourceText)
    {
        var startPosition = sourceText.Lines.GetPosition(new LinePosition(edit.StartLine - 1, edit.StartColumn - 1));
        var endPosition = sourceText.Lines.GetPosition(new LinePosition(edit.EndLine - 1, edit.EndColumn - 1));
        return TextSpan.FromBounds(startPosition, endPosition);
    }

    /// <summary>
    /// Applies <paramref name="edits"/> to <paramref name="sourceText"/> in memory (bottom-to-top),
    /// including line-break preservation for spans that end at column 1.
    /// </summary>
    private static SourceText BuildPatchedSourceText(SourceText sourceText, IReadOnlyList<TextEditDto> edits)
    {
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();
        var textChanges = new List<TextChange>();
        foreach (var edit in sortedEdits)
        {
            var startPosition = sourceText.Lines.GetPosition(new LinePosition(edit.StartLine - 1, edit.StartColumn - 1));
            var endPosition = sourceText.Lines.GetPosition(new LinePosition(edit.EndLine - 1, edit.EndColumn - 1));
            var span = TextSpan.FromBounds(startPosition, endPosition);

            var replacementText = edit.NewText;
            if (edit.EndColumn == 1 && span.Length > 0 && replacementText.Length > 0)
            {
                var lastCharInSpan = sourceText[span.End - 1];
                var endsWithNewline = replacementText[^1] is '\n' or '\r';
                if (lastCharInSpan is '\n' or '\r' && !endsWithNewline)
                {
                    var lineBreak = (span.End >= 2 && sourceText[span.End - 2] == '\r' && lastCharInSpan == '\n')
                        ? "\r\n"
                        : lastCharInSpan == '\n' ? "\n" : "\r";
                    replacementText += lineBreak;
                }
            }

            textChanges.Add(new TextChange(span, replacementText));
        }

        return sourceText.WithChanges(textChanges);
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
        SourceText newSourceText,
        CancellationToken ct)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var originalText = sourceText.ToString();

        var newDocument = document.WithText(newSourceText);
        var newSolution = newDocument.Project.Solution;

        var applied = _workspace.TryApplyChanges(workspaceId, newSolution);
        if (!applied)
        {
            return new TextEditResultDto(false, filePath, 0, [], null);
        }

        // BUG-N1: Mirror RefactoringService — MSBuildWorkspace may not flush text edits to disk.
        var persisted = await PersistDocumentTextToDiskAsync(workspaceId, normalizedPath, ct).ConfigureAwait(false);
        if (!persisted)
        {
            return new TextEditResultDto(false, filePath, 0, [], null);
        }

        // Compute bounded unified diff (hunk-based, 16 KB cap, truncation marker on overflow).
        // Previously emitted every line with "+ " / "- " / "  " prefixes — produced unbounded
        // output for large files and a format that was "unified-diff-like" rather than valid.
        var newText = newSourceText.ToString();
        var unified = DiffGenerator.GenerateUnifiedDiff(originalText, newText, filePath);
        var fileChange = new FileChangeDto(filePath, unified);

        _changeTracker?.RecordChange(workspaceId,
            $"Apply text edit to {Path.GetFileName(filePath)}",
            [filePath], "apply_text_edit");

        return new TextEditResultDto(true, filePath, edits.Count, [fileChange], null);
    }

    private static IReadOnlyList<TextEditSyntaxErrorDto> GetCSharpSyntaxErrors(SourceText newSourceText, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(newSourceText, path: filePath);
        var list = new List<TextEditSyntaxErrorDto>();
        foreach (var d in tree.GetDiagnostics())
        {
            if (d.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            var lineSpan = d.Location.GetLineSpan().StartLinePosition;
            list.Add(new TextEditSyntaxErrorDto(lineSpan.Line + 1, lineSpan.Character + 1, d.GetMessage()));
        }

        return list;
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
