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
        await RequireRecoveryAuthority().AuthorizePathsAsync(workspaceId, [canonicalWritePath ?? filePath], ct).ConfigureAwait(false);
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
            var syntaxErrors = GetCSharpSyntaxErrors(newSourceText, filePath);
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
        var physicalOperation = await CaptureMutationAsync(workspaceId, $"Apply text edit to {Path.GetFileName(filePath)}", fileSnapshots, ct).ConfigureAwait(false);
        var undo = _undoService is null ? null : await _undoService.PrepareBeforeApplyAsync(physicalOperation, ct).ConfigureAwait(false);
        var coreResult = await ApplyTextEditsCoreAsync(workspaceId, filePath, edits, solution,
            document, sourceText, newSourceText, fileSnapshots, physicalOperation, ct, pinnedWritePath).ConfigureAwait(false);
        if (!coreResult.Success || coreResult.Changes.Count == 0) return coreResult;
        if (undo is not null) await undo.PublishAsync(ct).ConfigureAwait(false);
        _changeTracker?.RecordChange(workspaceId, physicalOperation.Description,
            [filePath], toolName);
        if (!verify) return coreResult;
        var verification = await RunVerifyAndMaybeRevertAsync(workspaceId, projectFilter,
            preErrorBaseline!, autoRevertOnError, undo, ct).ConfigureAwait(false);
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
        await RequireRecoveryAuthority().AuthorizePathsAsync(workspaceId, fileEdits.Select(edit => edit.FilePath).Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
        var initial = _workspace.GetCurrentSolution(workspaceId);
        var (intended, changes) = await SimulatePreviewAsync(initial, fileEdits, ct, skipSyntaxCheck).ConfigureAwait(false);
        var projects = intended.GetChanges(initial).GetProjectChanges().Select(change => change.NewProject.Name)
            .Distinct(StringComparer.Ordinal).ToArray();
        var projectFilter = projects.Length == 1 ? projects[0] : null;
        var preBaseline = verify ? await CapturePreEditBaselineAsync(workspaceId, projectFilter, ct).ConfigureAwait(false) : null;
        var files = new List<FileSnapshotDto>();
        foreach (var path in changes.Select(change => change.FilePath).Distinct(FileSystemPath.Comparer))
        {
            var (document, text) = await ResolveDocumentAndTextAsync(initial, path, ct).ConfigureAwait(false);
            files.Add(await FileSnapshotCapture.CaptureAsync(document.FilePath
                ?? throw new InvalidOperationException("An edited document has no physical path."),
                () => text.ToString(), ct).ConfigureAwait(false));
        }
        var physicalOperation = await CaptureMutationAsync(workspaceId, $"Apply text edits to {fileEdits.Count} file(s)", files, ct).ConfigureAwait(false);
        var undo = _undoService is null ? null : await _undoService.PrepareBeforeApplyAsync(physicalOperation, ct).ConfigureAwait(false);
        var persisted = await new DocumentSetPersistenceService(_workspace, _logger).PersistAsync(
            workspaceId, initial, intended, intended.GetChanges(initial), files, physicalOperation, ct).ConfigureAwait(false);
        if (!persisted.Success) return new MultiFileEditResultDto(false, 0, []);
        var results = fileEdits.Select((edit, index) => new FileEditSummaryDto(edit.FilePath,
            string.IsNullOrEmpty(changes[index].UnifiedDiff) ? 0 : edit.Edits.Count,
            string.IsNullOrEmpty(changes[index].UnifiedDiff) ? null : changes[index].UnifiedDiff)).ToArray();
        if (persisted.AppliedFiles.Count == 0) return new MultiFileEditResultDto(true, 0, results);
        if (undo is not null) await undo.PublishAsync(ct).ConfigureAwait(false);
        _changeTracker?.RecordChange(workspaceId, physicalOperation.Description,
            persisted.AppliedFiles, toolName);
        var verification = verify ? await RunVerifyAndMaybeRevertAsync(workspaceId, projectFilter,
            preBaseline!, autoRevertOnError, undo, ct).ConfigureAwait(false) : null;
        return new MultiFileEditResultDto(true, persisted.AppliedFiles.Count, results, verification);
    }

    private IWorkspaceRecoveryAuthority RequireRecoveryAuthority() => _workspace as IWorkspaceRecoveryAuthority
        ?? throw InvalidOperationErrors.Internal("Text edits require workspace-owned physical recovery authority.");

    private Task<PhysicalMutationRecovery> CaptureMutationAsync(string workspaceId, string description, IReadOnlyList<FileSnapshotDto> files, CancellationToken ct) =>
        RequireRecoveryAuthority().CaptureRecoveryAsync(workspaceId, description, files, ct);

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
        var (accumulator, changes) =
            await SimulatePreviewAsync(initialSolution, fileEdits, ct, skipSyntaxCheck).ConfigureAwait(false);

        var description = $"Preview multi-file edit across {changes.Count} file(s)";
        // preview-token-apply-route-provenance: tag the token with its producer family so
        // preview_multi_file_edit_apply can refuse a token minted elsewhere before mutating.
        var token = _previewStore.Store(
            workspaceId,
            initialSolution,
            accumulator,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            kind: PreviewKind.MultiFileEdit);
        return new RefactoringPreviewDto(token, description, changes, Warnings: null);
    }

    /// <summary>
    /// Shared preview-orchestration body behind <see cref="PreviewMultiFileTextEditsAsync"/> and
    /// <see cref="PreviewMultiFileTextEditsOnSolutionAsync"/>, which previously carried
    /// line-for-line duplicates of it (<c>edit-preview-validation-decomposition</c>). Pre-validates
    /// every file's edits, then simulates them against a progressively accumulated
    /// <see cref="Solution"/> so each file's diff sees its predecessors' rewrites. Touches neither
    /// the workspace, the disk, nor <see cref="Contracts.IPreviewStore"/> — the two callers differ
    /// only in how they package this result.
    /// </summary>
    /// <remarks>
    /// Every edit is validated against its accumulated input before any disk write or token publication.
    /// </remarks>
    private static async Task<(Solution Accumulator, List<FileChangeDto> Changes)> SimulatePreviewAsync(
        Solution inputSolution, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck)
    {
        ArgumentNullException.ThrowIfNull(fileEdits);
        var accumulator = inputSolution;
        var changes = new List<FileChangeDto>(fileEdits.Count);
        foreach (var edit in fileEdits)
        {
            var (document, sourceText) = await ResolveDocumentAndTextAsync(accumulator, edit.FilePath, ct).ConfigureAwait(false);
            ValidateEdits(edit.Edits, sourceText);
            var merged = BuildPatchedSourceText(sourceText, edit.Edits);
            if (!skipSyntaxCheck && string.Equals(Path.GetExtension(edit.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                var errors = GetCSharpSyntaxErrors(merged, edit.FilePath);
                if (errors.Count > 0) throw new PublicInvalidOperationException(
                    $"Multi-file edits produce {errors.Count} syntax error(s). Pass skipSyntaxCheck=true if the intermediate state is intentional.");
            }
            accumulator = accumulator.WithDocumentText(document.Id, merged);
            changes.Add(new FileChangeDto(edit.FilePath,
                DiffGenerator.GenerateUnifiedDiff(sourceText.ToString(), merged.ToString(), edit.FilePath)));
        }
        if (changes.Count == 0) throw new PublicInvalidOperationException("A multi-file edit requires at least one file.");
        return (accumulator, changes);
    }

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
    internal async Task<(Solution NewSolution, IReadOnlyList<FileChangeDto> Changes, string Description, IReadOnlyList<string>? Warnings)>
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

        var (accumulator, changes) =
            await SimulatePreviewAsync(inputSolution, fileEdits, ct, skipSyntaxCheck).ConfigureAwait(false);

        var description = $"Preview multi-file edit across {changes.Count} file(s)";
        return (accumulator, changes, description, null);
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
    /// Callers that own a progressively mutated accumulator solution (multi-file apply / preview)
    /// pass their accumulator in directly — re-reading from the workspace manager at this layer
    /// would drop the in-progress edits. Entry-point callers that simply want the freshest
    /// solution should acquire it via <c>_workspace.GetCurrentSolution(...)</c> on the turn the
    /// resolve runs, so staleness-gate auto-reload results are reflected here.
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

    /// <summary>Persists one prepared edit without publishing undo or ledger state.</summary>
    private async Task<TextEditResultDto> ApplyTextEditsCoreAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits,
        Solution solution, Document document, SourceText sourceText, SourceText newSourceText,
        IReadOnlyList<FileSnapshotDto> files, PhysicalMutationRecovery physicalOperation, CancellationToken ct, string? canonicalWritePath)
    {
        if (canonicalWritePath is not null && !FileSystemPath.Comparer.Equals(document.FilePath, canonicalWritePath))
            throw new PublicInvalidOperationException("The pinned edit target differs from the loaded document. Reload and recreate the edit.");
        var modified = document.WithText(newSourceText).Project.Solution;
        var result = await new DocumentSetPersistenceService(_workspace, _logger).PersistAsync(
            workspaceId, solution, modified, modified.GetChanges(solution), files, physicalOperation, ct).ConfigureAwait(false);
        if (!result.Success) return new TextEditResultDto(false, filePath, 0, [], null);
        if (result.AppliedFiles.Count == 0) return new TextEditResultDto(true, filePath, 0, [], null);
        var change = new FileChangeDto(filePath,
            DiffGenerator.GenerateUnifiedDiff(sourceText.ToString(), newSourceText.ToString(), filePath));
        return new TextEditResultDto(true, filePath, edits.Count, [change], null);
    }

    private static IReadOnlyList<TextEditSyntaxErrorDto> GetCSharpSyntaxErrors(SourceText newSourceText, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(newSourceText, path: filePath);
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

        if (list.Count == 0 && root.ContainsSkippedText)
        {
            list.Add(new TextEditSyntaxErrorDto(
                1,
                1,
                "C# source contains parser recovered text (skipped tokens) without a listable top-level tree diagnostic. Pass skipSyntaxCheck=true if the intermediate state is intentional."));
        }

        return list;
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
    /// is false) or compensates through the exact undo operation captured by this call.
    /// A superseding operation refuses compensation.
    /// </summary>
    /// <remarks>
    /// Compensation uses the exact operation owned by this call; a superseding operation
    /// cannot redirect rollback to a different undo target.
    /// </remarks>
    private async Task<VerifyOutcomeDto> RunVerifyAndMaybeRevertAsync(
        string workspaceId,
        string? projectFilter,
        CompilationErrorSnapshot preEditBaseline,
        bool autoRevertOnError,
        IPreparedUndoOperation? undoOperation,
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

        // New errors require compensation through this call's owned undo operation.
        // A superseding operation cannot redirect it. _undoService may be null in
        // test contexts that construct EditService without an undo stack — surface
        // that as a structured outcome rather than a NullReferenceException.
        if (undoOperation is null)
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

        var reverted = await undoOperation.RestoreAsync(ct).ConfigureAwait(false);
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
