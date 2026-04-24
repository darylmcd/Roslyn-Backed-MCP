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
        bool autoRevertOnError = false)
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

        // semantic-edit-with-compile-check-wrapper: capture the pre-edit diagnostic
        // fingerprint set BEFORE we mutate the workspace so the verify pass can tell
        // NEW errors from pre-existing ones. Lives outside ApplyTextEditsCoreAsync
        // because MultiFile runs the capture once at the batch boundary.
        var projectFilter = verify ? document.Project.Name : null;
        var preErrorBaseline = verify
            ? await CapturePreEditBaselineAsync(workspaceId, projectFilter, ct).ConfigureAwait(false)
            : null;

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

        var coreResult = await ApplyTextEditsCoreAsync(workspaceId, filePath, edits, solution, document, sourceText, newSourceText, toolName, ct).ConfigureAwait(false);

        // Only wire up verify when the core apply actually wrote the edit. When the
        // core path returns Success=false (e.g. MSBuildWorkspace.TryApplyChanges
        // rejected the change), the undo entry still contains the pre-edit snapshot,
        // but nothing happened on disk — running compile_check here would add noise.
        if (!verify || !coreResult.Success)
        {
            return coreResult;
        }

        var verification = await RunVerifyAndMaybeRevertAsync(
            workspaceId,
            projectFilter,
            preErrorBaseline!,
            autoRevertOnError,
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

        // semantic-edit-with-compile-check-wrapper: pre-edit baseline runs ONCE across
        // the union of owning projects (project-level filter is still cheaper than a
        // full-solution compile). A null projectFilter means "compile every project"
        // — required when the batch spans more than one project.
        var ownerProjects = perFileSnapshots
            .Select(t => t.Document.Project.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var batchProjectFilter = verify && ownerProjects.Count == 1 ? ownerProjects[0] : null;
        var preErrorBaseline = verify
            ? await CapturePreEditBaselineAsync(workspaceId, batchProjectFilter, ct).ConfigureAwait(false)
            : null;

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
                workspaceId, fileEdit.FilePath, fileEdit.Edits, current, document, sourceText, merged, toolName, ct).ConfigureAwait(false);
            var diff = result.Changes.Count > 0
                ? string.Join("\n", result.Changes.Select(ch => ch.UnifiedDiff))
                : null;
            results.Add(new FileEditSummaryDto(fileEdit.FilePath, result.EditsApplied, diff));
        }

        VerifyOutcomeDto? verification = null;
        if (verify)
        {
            verification = await RunVerifyAndMaybeRevertAsync(
                workspaceId,
                batchProjectFilter,
                preErrorBaseline!,
                autoRevertOnError,
                ct).ConfigureAwait(false);
        }

        return new MultiFileEditResultDto(true, results.Count, results, verification);
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
        string toolName,
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
            [filePath], toolName);

        return new TextEditResultDto(true, filePath, edits.Count, [fileChange], null);
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

    // --------------------------------------------------------------------------
    // semantic-edit-with-compile-check-wrapper: verify + auto-revert support
    // --------------------------------------------------------------------------

    /// <summary>
    /// Captures a stable per-error fingerprint set for the current workspace so the
    /// post-edit verify pass can subtract pre-existing errors from the introduced set.
    /// Fingerprint format mirrors <c>ApplyWithVerifyTool.ExtractErrorFingerprints</c>:
    /// <c>id|file:line:col|message</c>. When <paramref name="projectFilter"/> is
    /// non-null, the baseline is scoped to that single project — cheaper and more
    /// precise than a full-solution compile for single-file edits.
    /// </summary>
    private async Task<PreEditBaseline> CapturePreEditBaselineAsync(
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

        // Page size of 500 covers most single-project repos. If a project legitimately
        // has more than 500 errors, the fingerprint set will be an over-count — not a
        // correctness hazard (any such error will also appear post-edit and be filtered
        // out), just a performance one.
        var baseline = await _compileCheckService.CheckAsync(
            workspaceId,
            new CompileCheckOptions(ProjectFilter: projectFilter, SeverityFilter: "error", Limit: 500),
            ct).ConfigureAwait(false);

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in baseline.Diagnostics)
        {
            if (!string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            fingerprints.Add(FormatErrorFingerprint(d));
        }

        return new PreEditBaseline(fingerprints, baseline.ErrorCount);
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
    /// method overwrote whatever was there. Running <c>RevertAsync</c> restores the
    /// pre-edit state that was captured inside this call's frame, not an earlier one.
    /// </remarks>
    private async Task<VerifyOutcomeDto> RunVerifyAndMaybeRevertAsync(
        string workspaceId,
        string? projectFilter,
        PreEditBaseline preEditBaseline,
        bool autoRevertOnError,
        CancellationToken ct)
    {
        // The null-check here would only fire in a misconfigured test DI; production
        // comes through AddRoslynMcpCoreServices which always wires CompileCheckService.
        // The CapturePreEditBaselineAsync path would have already thrown.
        ArgumentNullException.ThrowIfNull(_compileCheckService);

        var postCheck = await _compileCheckService.CheckAsync(
            workspaceId,
            new CompileCheckOptions(ProjectFilter: projectFilter, SeverityFilter: "error", Limit: 500),
            ct).ConfigureAwait(false);

        var newDiagnostics = new List<DiagnosticDto>();
        foreach (var d in postCheck.Diagnostics)
        {
            if (!string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var fingerprint = FormatErrorFingerprint(d);
            if (!preEditBaseline.ErrorFingerprints.Contains(fingerprint))
            {
                newDiagnostics.Add(d);
            }
        }

        if (newDiagnostics.Count == 0)
        {
            return new VerifyOutcomeDto(
                Status: "clean",
                PreErrorCount: preEditBaseline.ErrorCount,
                PostErrorCount: postCheck.ErrorCount,
                NewDiagnostics: Array.Empty<DiagnosticDto>(),
                ProjectFilter: projectFilter,
                Message: null);
        }

        if (!autoRevertOnError)
        {
            return new VerifyOutcomeDto(
                Status: "errors_introduced",
                PreErrorCount: preEditBaseline.ErrorCount,
                PostErrorCount: postCheck.ErrorCount,
                NewDiagnostics: newDiagnostics,
                ProjectFilter: projectFilter,
                Message: "The edit applied but introduced new compile errors. autoRevertOnError was false, " +
                        "so the workspace is preserved for inspection. Call revert_last_apply to roll back this edit.");
        }

        // autoRevertOnError=true AND new errors appeared. Roll back the single-slot
        // snapshot this call captured. _undoService may legitimately be null in
        // test contexts that construct EditService without an undo stack — surface
        // that as a structured outcome rather than a NullReferenceException.
        if (_undoService is null)
        {
            return new VerifyOutcomeDto(
                Status: "revert_failed",
                PreErrorCount: preEditBaseline.ErrorCount,
                PostErrorCount: postCheck.ErrorCount,
                NewDiagnostics: newDiagnostics,
                ProjectFilter: projectFilter,
                Message: "autoRevertOnError=true but IUndoService is not registered on this EditService. " +
                        "The edit remained applied. Wire IUndoService via AddRoslynMcpCoreServices.");
        }

        var reverted = await _undoService.RevertAsync(workspaceId, ct).ConfigureAwait(false);
        if (reverted)
        {
            return new VerifyOutcomeDto(
                Status: "reverted",
                PreErrorCount: preEditBaseline.ErrorCount,
                PostErrorCount: postCheck.ErrorCount,
                NewDiagnostics: newDiagnostics,
                ProjectFilter: projectFilter,
                Message: "The edit introduced new compile errors and was reverted. " +
                        "The workspace is back to the pre-edit state.");
        }

        return new VerifyOutcomeDto(
            Status: "revert_failed",
            PreErrorCount: preEditBaseline.ErrorCount,
            PostErrorCount: postCheck.ErrorCount,
            NewDiagnostics: newDiagnostics,
            ProjectFilter: projectFilter,
            Message: "The edit introduced new compile errors AND the auto-revert failed. " +
                    "The workspace is in an inconsistent state — inspect and call revert_last_apply manually.");
    }

    private static string FormatErrorFingerprint(DiagnosticDto d)
        => $"{d.Id}|{d.FilePath}:{d.StartLine}:{d.StartColumn}|{d.Message}";

    /// <summary>
    /// Holds the pre-edit fingerprint set plus the total pre-existing error count so the
    /// verify outcome can report both the delta and the baseline headline number.
    /// </summary>
    private sealed record PreEditBaseline(
        HashSet<string> ErrorFingerprints,
        int ErrorCount);
}
