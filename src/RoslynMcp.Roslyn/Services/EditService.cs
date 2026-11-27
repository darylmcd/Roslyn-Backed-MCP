using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class EditService : IEditService
{
    /// <summary>
    /// <see cref="ArgumentException.ParamName"/> reported by every edit-validation failure. Held as
    /// a constant so <see cref="ValidateEditBounds"/> — which sees one edit rather than the batch —
    /// keeps reporting the caller-visible <c>edits</c> parameter it was extracted from
    /// (<c>edit-preview-validation-decomposition</c>).
    /// </summary>
    private const string _editsParamName = "edits";

    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<EditService> _logger;
    private readonly IUndoService? _undoService;
    private readonly IChangeTracker? _changeTracker;
    private readonly Contracts.IPreviewStore? _previewStore;
    private readonly ICompileCheckService? _compileCheckService;

    public EditService(
        IWorkspaceManager workspace,
        ILogger<EditService> logger,
        IUndoService? undoService = null,
        IChangeTracker? changeTracker = null,
        Contracts.IPreviewStore? previewStore = null,
        ICompileCheckService? compileCheckService = null)
    {
        _workspace = workspace;
        _logger = logger;
        _undoService = undoService;
        _changeTracker = changeTracker;
        _previewStore = previewStore;
        _compileCheckService = compileCheckService;
    }

    public async Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId,
        string filePath,
        IReadOnlyList<TextEditDto> edits,
        string toolName,
        CancellationToken ct,
        bool skipSyntaxCheck = false,
        bool verify = false,
        bool autoRevertOnError = false,
        string? canonicalWritePath = null)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var (document, sourceText) = await ResolveDocumentAndTextAsync(solution, filePath, ct).ConfigureAwait(false);

        // apply-text-edit-invalid-edit-corrupt-diff: validate ranges up front so bad
        // edits throw a structured ArgumentException BEFORE we touch the file or the
        // unified diff. Without this, a reversed/out-of-bounds range could produce a
        // corrupt diff (ITChatBot audit I1, 2026-04-08).
        ValidateEdits(edits, sourceText);

        var newSourceText = BuildPatchedSourceText(sourceText, edits);
        if (!skipSyntaxCheck
            && string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            var syntaxErrors = GetCSharpSyntaxErrors(newSourceText, filePath, ResolveParseContexts(solution, document));
            if (syntaxErrors.Count > 0)
            {
                return new TextEditResultDto(false, filePath, 0, [], syntaxErrors);
            }
        }

        // semantic-edit-with-compile-check-wrapper: capture the pre-edit diagnostic
        // identity set BEFORE we mutate the workspace so the verify pass can tell
        // NEW errors from pre-existing ones. Identity = id|file|line (see
        // DiagnosticIdentitySet). Lives outside ApplyTextEditsCoreAsync because
        // MultiFile runs the capture once at the batch boundary.
        var projectFilter = verify ? document.Project.Name : null;
        var preErrorBaseline = verify
            ? await CapturePreEditBaselineAsync(workspaceId, projectFilter, ct).ConfigureAwait(false)
            : null;

        // Capture pre-apply snapshot so revert_last_apply can roll back this edit. We
        // pass BOTH the solution (for the legacy path) AND an explicit file snapshot
        // (for the authoritative file-based restore path — see FLAG-9A in UndoService).
        // Syntax check runs before capture so a rejected edit does not leave a no-op undo entry.
        // When path validation pinned a physical target, snapshot that SAME path used by the
        // write; storing the client path would let revert re-walk a subsequently swapped link.
        var normalizedFilePath = Path.GetFullPath(filePath);
        var pinnedWritePath = canonicalWritePath is null
            ? null
            : Path.GetFullPath(canonicalWritePath);
        var fileSnapshots = new[]
        {
            await FileSnapshotCapture.CaptureAsync(
                pinnedWritePath ?? normalizedFilePath,
                () => sourceText.ToString(),
                ct).ConfigureAwait(false),
        };
        var description = $"Apply text edit to {Path.GetFileName(filePath)}";
        var preparedUndo = _undoService?.Prepare(workspaceId, description, solution, fileSnapshots);
        var capture = UndoCaptureId.None;
        var workspaceApplied = false;
        TextEditResultDto coreResult;
        try
        {
            coreResult = await ApplyTextEditsCoreAsync(workspaceId, filePath, edits, document, sourceText, newSourceText,
                () => workspaceApplied = true, ct, canonicalWritePath: pinnedWritePath).ConfigureAwait(false);
        }
        finally
        {
            if (workspaceApplied && preparedUndo is not null)
                capture = _undoService!.Publish(preparedUndo, preparedUndo.FileSnapshots, UndoRecoveryMode.Full);
        }

        if (coreResult.Success)
            _changeTracker?.RecordChange(workspaceId, description, [filePath], toolName, capture);

        if (!verify || !coreResult.Success)
        {
            return coreResult;
        }

        var verification = await RunVerifyAndMaybeRevertAsync(
            workspaceId,
            projectFilter,
            preErrorBaseline!,
            autoRevertOnError,
            capture,
            ct).ConfigureAwait(false);

        return coreResult with { Verification = verification };
    }

    public async Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
        string workspaceId,
        IReadOnlyList<FileEditsDto> fileEdits,
        string toolName,
        CancellationToken ct,
        bool skipSyntaxCheck = false,
        bool verify = false,
        bool autoRevertOnError = false)
    {
        var initialSolution = _workspace.GetCurrentSolution(workspaceId);
        var perFileSnapshots = await ResolveBatchSnapshotsAsync(
            initialSolution,
            fileEdits,
            ct).ConfigureAwait(false);

        // semantic-edit-with-compile-check-wrapper: pre-edit baseline runs ONCE across
        // the union of owning projects (project-level filter is still cheaper than a
        // full-solution compile). A null projectFilter means "compile every project"
        // — required when the batch spans more than one project.
        var batchProjectFilter = verify ? ResolveSingleProjectFilter(perFileSnapshots) : null;
        var preErrorBaseline = verify
            ? await CapturePreEditBaselineAsync(workspaceId, batchProjectFilter, ct).ConfigureAwait(false)
            : null;

        // Single snapshot at the top so revert_last_apply rolls back the ENTIRE batch atomically
        // (from an undo perspective; individual disk writes still happen sequentially).
        var fileSnapshots = new List<FileSnapshotDto>(perFileSnapshots.Count);
        foreach (var (document, sourceText, _) in perFileSnapshots)
        {
            var targetPath = Path.GetFullPath(document.FilePath!);
            // Verification can run between target resolution and undo capture.
            WorkspacePhysicalPathGuard.EnsurePhysicalPath(targetPath);
            fileSnapshots.Add(await FileSnapshotCapture.CaptureAsync(
                targetPath,
                () => sourceText.ToString(),
                ct).ConfigureAwait(false));
        }
        var preparedUndo = _undoService?.Prepare(
            workspaceId, $"Apply edits to {fileEdits.Count} file(s)", initialSolution, fileSnapshots);
        var ownedSnapshots = new List<FileSnapshotDto>();
        var batchCapture = UndoCaptureId.None;
        List<TextEditResultDto> results;
        try
        {
            results = await ApplyBatchFilesAsync(
                workspaceId, perFileSnapshots, fileSnapshots, ownedSnapshots, skipSyntaxCheck, ct).ConfigureAwait(false);
        }
        finally
        {
            if (ownedSnapshots.Count > 0 && preparedUndo is not null)
                batchCapture = _undoService!.Publish(preparedUndo, ownedSnapshots, UndoRecoveryMode.Full);
        }

        // A normally completed batch with persisted edits promotes one undo entry.
        // An uncommitted residual stays pending without a ledger entry.
        if (results.Any(result => result.Success))
        {
            var batchAffectedFiles = ownedSnapshots.Select(snapshot => snapshot.FilePath).ToList();
            _changeTracker?.RecordChange(workspaceId, $"Apply text edits to {fileEdits.Count} file(s)",
                batchAffectedFiles, toolName, batchCapture);
        }

        VerifyOutcomeDto? verification = null;
        if (verify)
        {
            verification = await RunVerifyAndMaybeRevertAsync(
                workspaceId,
                batchProjectFilter,
                preErrorBaseline!,
                autoRevertOnError,
                batchCapture,
                ct).ConfigureAwait(false);
        }

        var summaries = results.Select(result => new FileEditSummaryDto(
            result.FilePath,
            result.EditsApplied,
            result.Changes.Count > 0
                ? string.Join("\n", result.Changes.Select(change => change.UnifiedDiff))
                : null)).ToList();
        return new MultiFileEditResultDto(
            results.All(result => result.Success),
            results.Count(result => result.Success),
            summaries,
            verification);
    }

    private static async Task<List<(Document Document, SourceText SourceText, FileEditsDto FileEdit)>> ResolveBatchSnapshotsAsync(
        Solution initialSolution,
        IReadOnlyList<FileEditsDto> fileEdits,
        CancellationToken ct)
    {
        var snapshots = new List<(Document, SourceText, FileEditsDto)>();
        var targets = new HashSet<DocumentId>();
        foreach (var fileEdit in fileEdits)
        {
            var (document, sourceText) = await ResolveDocumentAndTextAsync(
                initialSolution,
                fileEdit.FilePath,
                ct).ConfigureAwait(false);
            // Document resolution maps alternate spellings and directory links to the same target.
            if (!targets.Add(document.Id))
            {
                throw new PublicArgumentException("Combine edits for each file into one fileEdits entry.", nameof(fileEdits));
            }

            ValidateEdits(fileEdit.Edits, sourceText);
            snapshots.Add((document, sourceText, fileEdit));
        }

        return snapshots;
    }

    private static string? ResolveSingleProjectFilter(
        IEnumerable<(Document Document, SourceText SourceText, FileEditsDto FileEdit)> snapshots)
    {
        var ownerProjects = snapshots
            .Select(snapshot => snapshot.Document.Project.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return ownerProjects.Count == 1 ? ownerProjects[0] : null;
    }

    private async Task<List<TextEditResultDto>> ApplyBatchFilesAsync(
        string workspaceId,
        IReadOnlyList<(Document Document, SourceText SourceText, FileEditsDto FileEdit)> snapshots,
        IReadOnlyList<FileSnapshotDto> fileSnapshots,
        List<FileSnapshotDto> ownedSnapshots,
        bool skipSyntaxCheck,
        CancellationToken ct)
    {
        var results = new List<TextEditResultDto>();
        for (var index = 0; index < snapshots.Count; index++)
        {
            var (resolvedDocument, sourceText, fileEdit) = snapshots[index];
            var fileSnapshot = fileSnapshots[index];
            // Apply each delta to the latest solution without resolving the requested path again.
            var document = _workspace.GetCurrentSolution(workspaceId).GetDocument(resolvedDocument.Id)
                ?? throw new PublicNotFoundException("An edit target is no longer in the workspace. Reload the workspace and retry.");
            var merged = BuildPatchedSourceText(sourceText, fileEdit.Edits);
            if (!skipSyntaxCheck
                && string.Equals(Path.GetExtension(fileEdit.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)
                && GetCSharpSyntaxErrors(merged, fileEdit.FilePath, ResolveParseContexts(_workspace.GetCurrentSolution(workspaceId), document)).Count > 0)
            {
                results.Add(new TextEditResultDto(false, fileEdit.FilePath, 0, [], null));
                continue;
            }

            var result = await ApplyTextEditsCoreAsync(
                workspaceId,
                fileEdit.FilePath,
                fileEdit.Edits,
                document,
                sourceText,
                merged,
                () => ownedSnapshots.Add(fileSnapshot),
                ct,
                canonicalWritePath: Path.GetFullPath(resolvedDocument.FilePath!)).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Item 5: preview a multi-file edit batch without writing to disk. Simulates every file's
    /// edits against a single Roslyn <c>Solution</c> snapshot, stores the mutated snapshot in
    /// <see cref="Contracts.IPreviewStore"/>, and returns per-file unified diffs plus a
    /// multi-file preview token. Callers redeem via <c>preview_multi_file_edit_apply</c>.
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
        var (accumulator, edits) =
            await SimulatePreviewAsync(initialSolution, fileEdits, ct, skipSyntaxCheck).ConfigureAwait(false);

        var changes = edits.Select(edit => new FileChangeDto(edit.Path,
            DiffGenerator.GenerateUnifiedDiff(edit.Original.ToString(), edit.Patched.ToString(), edit.Path))).ToArray();
        var description = $"Preview multi-file edit across {edits.Count} file(s)";
        // preview-token-apply-route-provenance: tag the token with its producer family so
        // preview_multi_file_edit_apply can refuse a token minted elsewhere before mutating.
        // diffTruncated: false reproduces exactly what the untagged 4-argument overload forwarded.
        var token = _previewStore.Store(
            workspaceId,
            accumulator,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            diffTruncated: false,
            kind: PreviewKind.MultiFileEdit);
        return new RefactoringPreviewDto(token, description, changes, Warnings: null);
    }

    /// <summary>
    /// Shared preview-orchestration body behind <see cref="PreviewMultiFileTextEditsAsync"/> and
    /// <see cref="PreviewMultiFileTextEditsOnSolutionAsync"/>, which previously carried
    /// line-for-line duplicates of it (<c>edit-preview-validation-decomposition</c>). Pre-validates
    /// one edit group per resolved document against the input <see cref="Solution"/>, then
    /// accumulates each distinct file's result with its original text. Touches neither
    /// the workspace, the disk, nor <see cref="Contracts.IPreviewStore"/> — the two callers differ
    /// only in how they package this result.
    /// </summary>
    /// <remarks>
    /// Pre-validation runs to completion BEFORE any simulation so a malformed edit aborts the whole
    /// preview and the token-issuing caller never leaves a dangling token behind.
    /// </remarks>
    private static async Task<(Solution Accumulator, List<SimulatedFileEdit> Edits)> SimulatePreviewAsync(
        Solution inputSolution,
        IReadOnlyList<FileEditsDto> fileEdits,
        CancellationToken ct,
        bool skipSyntaxCheck)
    {
        var perFile = await ResolveBatchSnapshotsAsync(inputSolution, fileEdits, ct).ConfigureAwait(false);

        var accumulator = inputSolution;
        var edits = new List<SimulatedFileEdit>();
        foreach (var (document, sourceText, fileEdit) in perFile)
        {
            var filePath = fileEdit.FilePath;
            var merged = BuildPatchedSourceText(sourceText, fileEdit.Edits);
            if (!skipSyntaxCheck
                && string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                var syntaxErrors = GetCSharpSyntaxErrors(merged, filePath, ResolveParseContexts(inputSolution, document));
                if (syntaxErrors.Count > 0)
                {
                    throw new PublicInvalidOperationException(
                        $"Simulated edits produce {syntaxErrors.Count} C# syntax error(s). " +
                        "Correct the C# syntax and reissue the preview.");
                }
            }

            accumulator = accumulator.WithDocumentText(document.Id, merged);

            edits.Add(new SimulatedFileEdit(filePath, sourceText, merged));
        }

        return (accumulator, edits);
    }

    internal sealed record SimulatedFileEdit(string Path, SourceText Original, SourceText Patched);

    /// <summary>
    /// symbol-refactor-preview-auto-applies-without-explicit-apply-call: pure-functional
    /// multi-file-edit simulation that operates on an explicit input
    /// <paramref name="inputSolution"/> and returns the post-edit <see cref="Solution"/>
    /// snapshot. Mirrors <see cref="PreviewMultiFileTextEditsAsync"/>'s validation +
    /// accumulator semantics but never touches the workspace, the disk, or
    /// <see cref="Contracts.IPreviewStore"/>. Used by <see cref="SymbolRefactorService.PreviewAsync"/>
    /// to chain ops in-memory so each op sees its predecessor's rewrites without the previous
    /// auto-apply-each-step disk write that fired before the agent ever called
    /// <c>preview_multi_file_edit_apply</c>.
    /// </summary>
    internal async Task<(Solution NewSolution, IReadOnlyList<SimulatedFileEdit> Edits, string Description)>
        PreviewMultiFileTextEditsOnSolutionAsync(
            Solution inputSolution,
            IReadOnlyList<FileEditsDto> fileEdits,
            CancellationToken ct,
            bool skipSyntaxCheck = false)
    {
        if (fileEdits is null || fileEdits.Count == 0)
        {
            throw new InvalidOperationException("preview_multi_file_edit requires at least one file edit.");
        }

        var (accumulator, edits) =
            await SimulatePreviewAsync(inputSolution, fileEdits, ct, skipSyntaxCheck).ConfigureAwait(false);

        var description = $"Preview multi-file edit across {edits.Count} file(s)";
        return (accumulator, edits, description);
    }

    /// <summary>
    /// Resolves <paramref name="filePath"/> against the supplied <paramref name="solution"/> and
    /// reads the current <see cref="SourceText"/>. Uses the shared
    /// <see cref="DocumentResolution"/> helper so every preview / apply path raises a single
    /// consistent <see cref="InvalidOperationException"/> message when the file is not part of
    /// the workspace session. See
    /// <c>organize-usings-preview-document-not-found-after-apply</c>.
    /// </summary>
    /// <remarks>
    /// Batch callers resolve all targets against their input snapshot. A chained preview may pass
    /// the result of an earlier operation as that snapshot. Entry points that read the live
    /// workspace obtain its current solution before calling this helper, so they see any
    /// staleness-gate auto-reload.
    /// </remarks>
    private static async Task<(Document Document, SourceText SourceText)> ResolveDocumentAndTextAsync(
        Solution solution,
        string filePath,
        CancellationToken ct)
    {
        var document = DocumentResolution.GetDocumentInSolutionOrThrow(solution, filePath);
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
        IReadOnlyList<TextEditDto> edits,
        SourceText sourceText)
    {
        if (edits.Count == 0)
        {
            throw new PublicArgumentException("At least one text edit is required.", _editsParamName);
        }

        var lineCount = sourceText.Lines.Count;

        for (var i = 0; i < edits.Count; i++)
        {
            ValidateEditShape(i, edits[i]);
            ValidateEditBounds(i, edits[i], lineCount, sourceText);
        }

        ValidateNoOverlappingEdits(edits, sourceText);
    }

    /// <summary>
    /// First half of the per-edit checks extracted from <see cref="ValidateEdits"/>'s loop body
    /// (<c>edit-preview-validation-decomposition</c>): the two conditions that depend only on the
    /// edit itself, not on the document it targets — null <c>NewText</c>, then non-positive
    /// coordinates. Runs BEFORE <see cref="ValidateEditBounds"/> because a non-positive line index
    /// would otherwise be used to index into <see cref="SourceText.Lines"/>.
    /// </summary>
    /// <param name="index">Zero-based position of <paramref name="edit"/> in the caller's batch; used verbatim in error text.</param>
    private static void ValidateEditShape(int index, TextEditDto edit)
    {
        if (edit.NewText is null)
        {
            throw new PublicArgumentException(
                $"Edit #{index} has a null NewText. Use an empty string for deletions.",
                _editsParamName);
        }

        if (edit.StartLine < 1 || edit.StartColumn < 1 || edit.EndLine < 1 || edit.EndColumn < 1)
        {
            throw new PublicArgumentException(
                $"Edit #{index} has non-positive line/column: " +
                $"({edit.StartLine},{edit.StartColumn})-({edit.EndLine},{edit.EndColumn}). " +
                "Line and column are 1-based.",
                _editsParamName);
        }
    }

    /// <summary>
    /// Second half of the per-edit checks extracted from <see cref="ValidateEdits"/>'s loop body
    /// (<c>edit-preview-validation-decomposition</c>): the conditions that measure the edit against
    /// the target document — out-of-range line, StartColumn overflow, EndColumn overflow, then
    /// reversed range. Assumes <see cref="ValidateEditShape"/> already ran, so every coordinate is
    /// positive and safe to use as a 1-based index. The branch ORDER is load-bearing: callers
    /// depend on the first violation in that sequence being the one reported.
    /// </summary>
    /// <param name="index">Zero-based position of <paramref name="edit"/> in the caller's batch; used verbatim in error text.</param>
    private static void ValidateEditBounds(
        int index,
        TextEditDto edit,
        int lineCount,
        SourceText sourceText)
    {
        if (edit.StartLine > lineCount || edit.EndLine > lineCount)
        {
            throw new PublicArgumentException(
                $"Edit #{index} references line {Math.Max(edit.StartLine, edit.EndLine)} " +
                $"but the file only has {lineCount} line(s).",
                _editsParamName);
        }

        var startLineLength = sourceText.Lines[edit.StartLine - 1].SpanIncludingLineBreak.Length;
        if (edit.StartColumn > startLineLength + 1)
        {
            throw new PublicArgumentException(
                $"Edit #{index} has StartColumn {edit.StartColumn} but line {edit.StartLine} " +
                $"only has {startLineLength} character(s). Columns are 1-based and may be one past the end.",
                _editsParamName);
        }

        var endLineLength = sourceText.Lines[edit.EndLine - 1].SpanIncludingLineBreak.Length;
        if (edit.EndColumn > endLineLength + 1)
        {
            throw new PublicArgumentException(
                $"Edit #{index} has EndColumn {edit.EndColumn} but line {edit.EndLine} " +
                $"only has {endLineLength} character(s).",
                _editsParamName);
        }

        if (edit.StartLine > edit.EndLine
            || (edit.StartLine == edit.EndLine && edit.StartColumn > edit.EndColumn))
        {
            throw new PublicArgumentException(
                $"Edit #{index} has a reversed range: " +
                $"start ({edit.StartLine},{edit.StartColumn}) is after end ({edit.EndLine},{edit.EndColumn}). " +
                "Zero-width ranges are allowed (inserts) but the end position must not precede the start.",
                _editsParamName);
        }
    }

    /// <summary>
    /// apply-text-edit-overlap: Overlapping spans passed to <see cref="SourceText.WithChanges"/>
    /// produce undefined merge behavior. Reject before any mutation.
    /// </summary>
    private static void ValidateNoOverlappingEdits(IReadOnlyList<TextEditDto> edits, SourceText sourceText)
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
                throw new PublicArgumentException(
                    $"Edits #{left.Index} and #{right.Index} have overlapping spans: " +
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

            var replacementText = AdjustReplacementForTrailingLineBreak(sourceText, span, edit.EndColumn, edit.NewText);
            textChanges.Add(new TextChange(span, replacementText));
        }

        return sourceText.WithChanges(textChanges);
    }

    /// <summary>
    /// Line-break preservation for a replacement whose span ends at column 1, extracted from
    /// <see cref="BuildPatchedSourceText"/> (<c>edit-preview-validation-decomposition</c>). A span
    /// ending at column 1 swallows the previous line's terminator, so a replacement that does not
    /// itself end in a newline gets the swallowed terminator re-appended — preserving the file's
    /// existing <c>\r\n</c> / <c>\n</c> / <c>\r</c> convention at that position.
    /// </summary>
    /// <remarks>
    /// The <c>\r\n</c>-before-<c>\n</c>-before-<c>\r</c> detection order is load-bearing: probing
    /// for the paired <c>\r</c> first is what keeps CRLF files from degrading to bare LF. Reordering
    /// these branches silently corrupts line endings with no compile-time signal.
    /// </remarks>
    /// <returns><paramref name="replacementText"/> unchanged, or with the preserved terminator appended.</returns>
    private static string AdjustReplacementForTrailingLineBreak(
        SourceText sourceText,
        TextSpan span,
        int editEndColumn,
        string replacementText)
    {
        if (editEndColumn != 1 || span.Length == 0 || replacementText.Length == 0)
        {
            return replacementText;
        }

        // A replacement that already carries its own terminator needs no help.
        if (replacementText[^1] is '\n' or '\r')
        {
            return replacementText;
        }

        return replacementText + GetSwallowedLineBreak(sourceText, span);
    }

    /// <summary>
    /// The line terminator (if any) consumed by the tail of <paramref name="span"/>, as it appears
    /// in <paramref name="sourceText"/>. Returns <see cref="string.Empty"/> when the span does not
    /// end on a terminator. Split out of
    /// <see cref="AdjustReplacementForTrailingLineBreak"/> (<c>edit-preview-validation-decomposition</c>).
    /// </summary>
    /// <remarks>
    /// The <c>\n</c> case must probe the preceding character for a paired <c>\r</c> before settling
    /// for a bare <c>\n</c> — that probe is the only thing keeping CRLF files from degrading to
    /// mixed line endings, and the degradation carries no compile-time or syntax-check signal.
    /// </remarks>
    private static string GetSwallowedLineBreak(SourceText sourceText, TextSpan span)
    {
        var lastCharInSpan = sourceText[span.End - 1];
        if (lastCharInSpan == '\n')
        {
            return span.End >= 2 && sourceText[span.End - 2] == '\r' ? "\r\n" : "\n";
        }

        return lastCharInSpan == '\r' ? "\r" : string.Empty;
    }

    /// <summary>
    /// Inner edit-application path that does NOT touch the undo stack. The caller is
    /// responsible for snapshotting before invoking this method (single-file path
    /// snapshots once; multi-file path snapshots once at the batch boundary).
    /// </summary>
    /// <param name="onWorkspaceApplied">Records acceptance before disk persistence.</param>
    /// <param name="canonicalWritePath">
    /// Optional boundary-canonicalized target the physical write must land on, pinned by the caller's
    /// path validation (<c>path-boundary-link-swap-toctou</c>). <c>null</c> writes to the resolved
    /// document's own path.
    /// </param>
    private async Task<TextEditResultDto> ApplyTextEditsCoreAsync(
        string workspaceId,
        string filePath,
        IReadOnlyList<TextEditDto> edits,
        Document document,
        SourceText sourceText,
        SourceText newSourceText,
        Action onWorkspaceApplied,
        CancellationToken ct,
        string? canonicalWritePath = null)
    {
        var originalText = sourceText.ToString();

        var newDocument = document.WithText(newSourceText);
        var newSolution = newDocument.Project.Solution;

        var applied = _workspace.TryApplyChanges(workspaceId, newSolution);
        if (!applied)
        {
            return new TextEditResultDto(false, filePath, 0, [], null);
        }

        onWorkspaceApplied();

        // BUG-N1: Mirror RefactoringService — MSBuildWorkspace may not flush text edits to disk.
        var persisted = await PersistDocumentTextToDiskAsync(workspaceId, document.Id, ct, canonicalWritePath).ConfigureAwait(false);
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

        return new TextEditResultDto(true, filePath, edits.Count, [fileChange], null);
    }

    /// <summary>
    /// edit-syntax-check-project-parse-options: the distinct parse options of every loaded context of
    /// <paramref name="document"/>'s file (one per project it is compiled in, so a linked or
    /// multi-targeted file yields several), normalized so only the language version, preprocessor symbols
    /// and project features differ from the default parse. <see cref="DocumentationMode.Parse"/> and
    /// <see cref="SourceCodeKind.Regular"/> are pinned: a project that generates documentation would
    /// otherwise surface malformed-XML-doc warnings as syntax diagnostics and refuse edits this check never
    /// refused. A document without C# options (not expected for a .cs file) falls back to the defaults.
    /// </summary>
    private static IReadOnlyList<CSharpParseOptions> ResolveParseContexts(Solution solution, Document document)
    {
        var contexts = new List<CSharpParseOptions>();
        var seen = new HashSet<CSharpParseOptions>();
        var contextDocuments = document.FilePath is null
            ? new[] { document }
            : SymbolResolver.FindDocuments(solution, document.FilePath).Prepend(document);
        foreach (var contextDocument in contextDocuments)
        {
            if (contextDocument.Project.ParseOptions is not CSharpParseOptions options)
            {
                continue;
            }

            var normalized = options.WithKind(SourceCodeKind.Regular).WithDocumentationMode(DocumentationMode.Parse);
            if (seen.Add(normalized))
            {
                contexts.Add(normalized);
            }
        }

        if (contexts.Count == 0)
        {
            contexts.Add(CSharpParseOptions.Default);
        }

        return contexts;
    }

    /// <summary>
    /// Syntax errors of <paramref name="newSourceText"/> under every context in <paramref name="contexts"/>:
    /// a file compiled in several projects must be clean in each, because the edit writes one physical file.
    /// Diagnostics are deduplicated on position and message; the first context's order is kept, so a
    /// single-context file reports exactly what the check reported before.
    /// </summary>
    private static IReadOnlyList<TextEditSyntaxErrorDto> GetCSharpSyntaxErrors(
        SourceText newSourceText, string filePath, IReadOnlyList<CSharpParseOptions> contexts)
    {
        var merged = new List<TextEditSyntaxErrorDto>();
        foreach (var context in contexts)
        {
            foreach (var error in GetCSharpSyntaxErrors(newSourceText, filePath, context))
            {
                if (!merged.Contains(error))
                {
                    merged.Add(error);
                }
            }
        }

        return merged;
    }

    private static IReadOnlyList<TextEditSyntaxErrorDto> GetCSharpSyntaxErrors(
        SourceText newSourceText, string filePath, CSharpParseOptions options)
    {
        var tree = CSharpSyntaxTree.ParseText(newSourceText, options, path: filePath);
        var root = tree.GetRoot();
        var list = new List<TextEditSyntaxErrorDto>();
        foreach (var d in tree.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Hidden)
            {
                continue;
            }

            // #warning in source (CS1030) is a directive diagnostic, not a malformed-tree
            // signal; blocking apply on it would false-positive for intentional warnings.
            if (d is { Severity: DiagnosticSeverity.Warning, Id: "CS1030" })
            {
                continue;
            }

            // A standalone syntax tree's diagnostics are lexer/parser (plus directive)
            // only. The prior filter (Error only) could accept invalid C# when Roslyn's
            // recovery path reported only non-Error severities, or when skipped tokens did
            // not re-surface on the whole tree. Treat other non-Hidden tree diagnostics
            // and skipped text as a syntax check failure. Callers that need a deliberate
            // intermediate can pass skipSyntaxCheck=true.
            var lineSpan = d.Location.GetLineSpan().StartLinePosition;
            list.Add(new TextEditSyntaxErrorDto(lineSpan.Line + 1, lineSpan.Character + 1, d.GetMessage()));
        }

        // ContainsSkippedText also covers legal directive messages (e.g. a #region
        // label). Only actual recovered tokens trigger this diagnostic-free fallback.
        if (list.Count == 0 && root.ContainsSkippedText &&
            root.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.SkippedTokensTrivia)))
        {
            list.Add(new TextEditSyntaxErrorDto(
                1,
                1,
                "C# source contains parser recovered text (skipped tokens) without a listable top-level tree diagnostic. Pass skipSyntaxCheck=true if the intermediate state is intentional."));
        }

        return list;
    }

    /// <summary>
    /// Flushes the workspace document's current text to disk.
    /// </summary>
    /// <param name="canonicalWritePath">
    /// When non-null, the boundary-canonicalized target the caller's path validation already pinned;
    /// the write lands there instead of on <c>document.FilePath</c>
    /// (<c>workspace-load-path-canonicalization</c>, <c>path-boundary-link-swap-toctou</c>).
    /// The already-resolved document supplies the updated text and its encoding.
    /// </param>
    private async Task<bool> PersistDocumentTextToDiskAsync(
        string workspaceId,
        DocumentId documentId,
        CancellationToken ct,
        string? canonicalWritePath = null)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = solution.GetDocument(documentId);

        if (document?.FilePath is null)
            return false;

        try
        {
            // mutation-write-paths-drop-original-encoding: keep the SourceText object rather than
            // collapsing it to a string immediately — SourceText.Encoding carries the encoding
            // Roslyn detected when the document was loaded from disk (and SourceText.WithChanges
            // propagates it through the edit), so threading it into the write keeps a UTF-8-BOM or
            // UTF-16 source file byte-faithful instead of silently re-encoding it as UTF-8-no-BOM.
            var sourceText = await document.GetTextAsync(ct).ConfigureAwait(false);
            await AtomicFileWriter.WriteAllTextAsync(
                canonicalWritePath ?? document.FilePath,
                sourceText.ToString(),
                ct,
                logger: _logger,
                encoding: SourceFileEncoding.FromSourceText(sourceText.Encoding)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to persist document to disk: {Path}", document.FilePath);
            return false;
        }
    }

    // --------------------------------------------------------------------------
    // semantic-edit-with-compile-check-wrapper: verify + auto-revert support
    // --------------------------------------------------------------------------

    /// <summary>
    /// Captures a stable per-error identity set for the current workspace so the
    /// post-edit verify pass can subtract pre-existing errors from the introduced set.
    /// Identity format is supplied by <see cref="DiagnosticIdentitySet"/>: <c>id|file|line</c>
    /// (apply-with-verify-diff-not-counts). The previous fingerprint included column AND
    /// message text — both can flip across the pre-vs-post pair without the apply being
    /// to blame, producing false-positive rollbacks. When <paramref name="projectFilter"/> is
    /// non-null, the baseline is scoped to that single project — cheaper and more
    /// precise than a full-solution compile for single-file edits.
    /// </summary>
    private async Task<CompilationErrorSnapshot> CapturePreEditBaselineAsync(
        string workspaceId,
        string? projectFilter,
        CancellationToken ct)
    {
        if (_compileCheckService is null)
        {
            // verify was requested but the service is not wired — surface a structured
            // message instead of silently skipping. The caller asked for verify; they
            // deserve to know why there is no outcome to inspect.
            throw new InvalidOperationException(
                "apply_text_edit/apply_multi_file_edit verify=true requires ICompileCheckService to be registered. " +
                "Ensure RoslynMcp.Roslyn DI is configured (AddRoslynMcpCoreServices).");
        }

        return await CompilationVerification.CaptureAsync(
            _compileCheckService,
            workspaceId,
            projectFilter,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Post-edit verify leg: runs <c>compile_check</c> on the same scope as the baseline,
    /// subtracts the pre-existing errors to produce the introduced-error set, then either
    /// returns the verify outcome (when no new errors OR <paramref name="autoRevertOnError"/>
    /// is false) or calls <c>revert_last_apply</c> to roll back the single-slot snapshot
    /// this call just captured.
    /// </summary>
    /// <remarks>
    /// Single-shot semantics: this revert targets ONLY the snapshot that the current
    /// call placed on the undo stack. Prior-turn edits are never touched — the undo
    /// service is already single-slot per workspace, so the capture earlier in this
    /// method overwrote whatever was there. Reverting the matching capture restores the
    /// pre-edit state that was captured inside this call's frame, not an earlier one.
    /// </remarks>
    private async Task<VerifyOutcomeDto> RunVerifyAndMaybeRevertAsync(
        string workspaceId,
        string? projectFilter,
        CompilationErrorSnapshot preEditBaseline,
        bool autoRevertOnError,
        UndoCaptureId capture,
        CancellationToken ct)
    {
        // The null-check here would only fire in a misconfigured test DI; production
        // comes through AddRoslynMcpCoreServices which always wires CompileCheckService.
        // The CapturePreEditBaselineAsync path would have already thrown.
        ArgumentNullException.ThrowIfNull(_compileCheckService);

        var postEditSnapshot = await CompilationVerification.CaptureAsync(
            _compileCheckService,
            workspaceId,
            projectFilter,
            ct).ConfigureAwait(false);

        var newDiagnostics = CompilationVerification.FindIntroducedDiagnostics(
            preEditBaseline,
            postEditSnapshot);

        if (newDiagnostics.Count == 0)
        {
            return CreateVerifyOutcome(
                "clean",
                preEditBaseline,
                postEditSnapshot,
                [],
                projectFilter,
                message: null);
        }

        if (!autoRevertOnError)
        {
            return CreateVerifyOutcome(
                "errors_introduced",
                preEditBaseline,
                postEditSnapshot,
                newDiagnostics,
                projectFilter,
                "The edit applied but introduced new compile errors. autoRevertOnError was false, " +
                "so the workspace is preserved for inspection. Call revert_last_apply to roll back this edit.");
        }

        // autoRevertOnError=true AND new errors appeared. Roll back the single-slot
        // snapshot this call captured. _undoService may legitimately be null in
        // test contexts that construct EditService without an undo stack — surface
        // that as a structured outcome rather than a NullReferenceException.
        if (_undoService is null)
        {
            return CreateVerifyOutcome(
                "revert_failed",
                preEditBaseline,
                postEditSnapshot,
                newDiagnostics,
                projectFilter,
                "autoRevertOnError=true but IUndoService is not registered on this EditService. " +
                "The edit remained applied. Wire IUndoService via AddRoslynMcpCoreServices.");
        }

        var reverted = !capture.IsNone && await _undoService.RevertCaptureAsync(workspaceId, capture, ct).ConfigureAwait(false);
        return reverted
            ? CreateVerifyOutcome(
                "reverted",
                preEditBaseline,
                postEditSnapshot,
                newDiagnostics,
                projectFilter,
                "The edit introduced new compile errors and was reverted. " +
                "The workspace is back to the pre-edit state.")
            : CreateVerifyOutcome(
                "revert_failed",
                preEditBaseline,
                postEditSnapshot,
                newDiagnostics,
                projectFilter,
                "The edit introduced new compile errors AND the auto-revert failed. " +
                "The workspace is in an inconsistent state — inspect and call revert_last_apply manually.");
    }

    private static VerifyOutcomeDto CreateVerifyOutcome(
        string status,
        CompilationErrorSnapshot preEditBaseline,
        CompilationErrorSnapshot postEditSnapshot,
        IReadOnlyList<DiagnosticDto> newDiagnostics,
        string? projectFilter,
        string? message) =>
        new(
            Status: status,
            PreErrorCount: preEditBaseline.ErrorCount,
            PostErrorCount: postEditSnapshot.ErrorCount,
            NewDiagnostics: newDiagnostics,
            ProjectFilter: projectFilter,
            Message: message);

}
