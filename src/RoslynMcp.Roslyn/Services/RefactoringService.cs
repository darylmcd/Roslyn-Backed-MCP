using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Coordinates Roslyn-based refactoring operations with preview/apply semantics,
/// workspace versioning, and undo support. Handles rename, organize usings, format,
/// and code fix operations.
/// </summary>
public sealed class RefactoringService : IRefactoringService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly IUndoService? _undoService;
    private readonly IChangeTracker? _changeTracker;
    private readonly ICodeFixProviderRegistry? _codeFixRegistry;
    private readonly IPostApplySymbolResolver? _postApplyResolver;
    private readonly DocumentSetPersistenceService _documentSetPersistence;
    private readonly ILogger<RefactoringService> _logger;

    public RefactoringService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<RefactoringService> logger, IUndoService? undoService = null, IChangeTracker? changeTracker = null, ICodeFixProviderRegistry? codeFixRegistry = null, IPostApplySymbolResolver? postApplyResolver = null)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _undoService = undoService;
        _changeTracker = changeTracker;
        _codeFixRegistry = codeFixRegistry;
        _postApplyResolver = postApplyResolver;
        _logger = logger;
        _documentSetPersistence = new DocumentSetPersistenceService(workspace, logger);
    }

    /// <summary>
    /// Previews renaming a symbol and all its references across the solution.
    /// </summary>
    public Task<RefactoringPreviewDto> PreviewRenameAsync(
        string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
        => PreviewRenameAsync(workspaceId, locator, newName, summary: false, ct);

    public async Task<RefactoringPreviewDto> PreviewRenameAsync(
        string workspaceId, SymbolLocator locator, string newName, bool summary, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
            throw new InvalidOperationException(BuildRenameTargetNotFoundMessage(locator));

        if (!symbol.Locations.Any(static l => l.IsInSource))
        {
            throw new InvalidOperationException(
                $"Cannot rename metadata or built-in symbol '{symbol.ToDisplayString()}' — renames require a source declaration.");
        }

        // Reject illegal identifiers BEFORE invoking Renamer so we never produce a preview
        // whose application would break compilation across the solution.
        IdentifierValidation.ThrowIfInvalidIdentifier(newName);

        var newSolution = await Renamer.RenameSymbolAsync(
            solution, symbol, new SymbolRenameOptions(), newName, ct).ConfigureAwait(false);

        // Item #8 — rename-preview-output-cap-high-fan-out-symbols. The full unified
        // diffs scale with reference fan-out (a 233-ref symbol produces ~98 KB of diff
        // text); on symbols with >200 refs the payload exceeds the MCP output cap. In
        // summary mode we replace the per-file UnifiedDiff with a compact descriptor
        // while the stored Solution still carries every actual edit, so a subsequent
        // apply rewrites every reference correctly.
        IReadOnlyList<FileChangeDto> changes;
        if (summary)
        {
            changes = await BuildRenameSummaryChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        }
        else
        {
            changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        }

        var description = $"Rename '{symbol.Name}' to '{newName}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.SymbolRename);

        // Register a post-apply rename hint so ApplyRefactoringAsync can rotate the handle
        // and return ApplyResult.MutatedSymbol with the new name. Declaration position is
        // stable across renames, so stashing it plus the expected new name is sufficient.
        if (_postApplyResolver is not null)
        {
            var declLocation = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
            if (declLocation is not null)
            {
                var declLineSpan = declLocation.GetLineSpan();
                _postApplyResolver.RegisterRename(
                    token,
                    new PostApplyRenameHint(
                        declLineSpan.Path,
                        declLineSpan.StartLinePosition.Line + 1,
                        declLineSpan.StartLinePosition.Character + 1,
                        newName));
            }
        }

        // No-op warning: caller asked to rename a symbol to its own current name. C# identifiers
        // are case-sensitive, so a Foo→foo rename is real and must NOT be flagged.
        IReadOnlyList<string>? warnings = null;
        if (string.Equals(symbol.Name, newName, StringComparison.Ordinal))
        {
            warnings = new[] { $"New name '{newName}' matches the existing name; no changes were produced." };
        }

        return new RefactoringPreviewDto(token, description, changes, warnings);
    }

    /// <summary>
    /// symbol-refactor-preview-auto-applies-without-explicit-apply-call: pure-functional
    /// rename simulation that operates on an explicit input <paramref name="inputSolution"/>
    /// and returns the post-rename <see cref="Solution"/> snapshot without touching the
    /// workspace, the disk, the preview store, the change tracker, or any post-apply
    /// resolver. Used by <see cref="SymbolRefactorService.PreviewAsync"/> to chain ops
    /// in-memory so each op sees its predecessor's rewrites without the previous
    /// auto-apply-each-step disk write that fired before the agent ever called
    /// <c>apply_composite_preview</c>.
    /// </summary>
    /// <remarks>
    /// Returned tuple carries the modified solution plus a per-file <see cref="FileChangeDto"/>
    /// list (full-diff form — summary mode is not relevant here because the composite caller
    /// aggregates diffs across ops before applying its own truncation) and a human-readable
    /// step description suitable for the composite preview's combined description string.
    /// </remarks>
    internal async Task<(Solution NewSolution, IReadOnlyList<FileChangeDto> Changes, string Description)>
        PreviewRenameOnSolutionAsync(
            Solution inputSolution, SymbolLocator locator, string newName, CancellationToken ct)
    {
        var symbol = await SymbolResolver.ResolveAsync(inputSolution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
            throw new InvalidOperationException(BuildRenameTargetNotFoundMessage(locator));

        if (!symbol.Locations.Any(static l => l.IsInSource))
        {
            throw new InvalidOperationException(
                $"Cannot rename metadata or built-in symbol '{symbol.ToDisplayString()}' — renames require a source declaration.");
        }

        IdentifierValidation.ThrowIfInvalidIdentifier(newName);

        var newSolution = await Renamer.RenameSymbolAsync(
            inputSolution, symbol, new SymbolRenameOptions(), newName, ct).ConfigureAwait(false);

        var changes = await SolutionDiffHelper.ComputeChangesAsync(inputSolution, newSolution, ct).ConfigureAwait(false);
        var description = $"Rename '{symbol.Name}' to '{newName}'";

        return (newSolution, changes, description);
    }

    private static string BuildRenameTargetNotFoundMessage(SymbolLocator locator)
    {
        if (locator.HasMetadataName)
        {
            return $"No symbol found for metadataName '{locator.MetadataName}'. " +
                   "For member renames, pass a fully qualified target such as Namespace.Type.Member or ContainingType.Member, " +
                   "or use a symbolHandle from document_symbols/enclosing_symbol for precise targeting.";
        }

        if (locator.HasHandle)
        {
            return "No symbol found for the supplied symbolHandle. The handle may be from a previous workspace version; " +
                   "refresh it with document_symbols or enclosing_symbol before retrying.";
        }

        return $"No symbol found at {locator.FilePath}:{locator.Line}:{locator.Column}. " +
               "Place the location on the identifier to rename, or use a symbolHandle from document_symbols/enclosing_symbol.";
    }

    /// <summary>
    /// Item #8 — compact per-file summaries for summary=true. Computes per-file
    /// added/removed line counts from the Solution diff without serializing full
    /// unified-diff hunk bodies. Keeps each FileChangeDto's UnifiedDiff to a single
    /// human-readable line so the total response stays well under the MCP output cap
    /// even on 200+ reference symbols.
    /// </summary>
    private static async Task<IReadOnlyList<FileChangeDto>> BuildRenameSummaryChangesAsync(
        Solution oldSolution, Solution newSolution, CancellationToken ct)
    {
        var summaries = new List<FileChangeDto>();

        foreach (var projectChange in newSolution.GetChanges(oldSolution).GetProjectChanges())
        {
            await AppendChangedDocumentSummariesAsync(
                summaries,
                oldSolution,
                newSolution,
                projectChange,
                ct).ConfigureAwait(false);
            await AppendAddedDocumentSummariesAsync(
                summaries,
                newSolution,
                projectChange,
                ct).ConfigureAwait(false);
            AppendRemovedDocumentSummaries(summaries, oldSolution, projectChange);
        }

        return summaries;
    }

    private static async Task AppendChangedDocumentSummariesAsync(
        ICollection<FileChangeDto> summaries,
        Solution oldSolution,
        Solution newSolution,
        ProjectChanges projectChange,
        CancellationToken ct)
    {
        foreach (var documentId in projectChange.GetChangedDocuments())
        {
            var oldDocument = oldSolution.GetDocument(documentId);
            var newDocument = newSolution.GetDocument(documentId);
            if (oldDocument is null || newDocument is null)
            {
                continue;
            }

            var oldText = (await oldDocument.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            var newText = (await newDocument.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            if (string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                continue;
            }

            var oldLineCount = CountLines(oldText);
            var newLineCount = CountLines(newText);
            var filePath = oldDocument.FilePath ?? newDocument.FilePath ?? oldDocument.Name;
            var netChange = newLineCount - oldLineCount;
            var netMarker = netChange switch
            {
                > 0 => $"+{netChange} lines",
                < 0 => $"{netChange} lines",
                _ => "no net line change",
            };

            summaries.Add(new FileChangeDto(
                filePath,
                $"# summary=true: {oldLineCount} → {newLineCount} lines ({netMarker}). " +
                "Full unified diff suppressed to keep the response under the MCP output cap; " +
                "pass summary=false to see per-site edits."));
        }
    }

    private static async Task AppendAddedDocumentSummariesAsync(
        ICollection<FileChangeDto> summaries,
        Solution newSolution,
        ProjectChanges projectChange,
        CancellationToken ct)
    {
        foreach (var documentId in projectChange.GetAddedDocuments())
        {
            var document = newSolution.GetDocument(documentId);
            if (document is null)
            {
                continue;
            }

            var path = document.FilePath ?? document.Name;
            var text = await document.GetTextAsync(ct).ConfigureAwait(false);
            summaries.Add(new FileChangeDto(
                path,
                $"# summary=true: added file ({CountLines(text.ToString())} lines)."));
        }
    }

    private static void AppendRemovedDocumentSummaries(
        ICollection<FileChangeDto> summaries,
        Solution oldSolution,
        ProjectChanges projectChange)
    {
        foreach (var documentId in projectChange.GetRemovedDocuments())
        {
            var document = oldSolution.GetDocument(documentId);
            if (document is not null)
            {
                summaries.Add(new FileChangeDto(
                    document.FilePath ?? document.Name,
                    "# summary=true: removed file."));
            }
        }
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') count++;
        }
        return count;
    }

    /// <summary>
    /// Applies a previously previewed refactoring. Validates the preview token against the current
    /// workspace version to reject stale changes.
    /// </summary>
    public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
        => ApplyRefactoringAsync(previewToken, toolName, force: false, ct);

    public async Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, bool force, CancellationToken ct)
    {
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return InvalidPreviewResult();
        }

        var (workspaceId, originalSolution, modifiedSolution, _, description, diffTruncated) = entry.Value;
        // preview-token-cross-coupling-bundle (BREAKING): version-equality check removed.
        // Each token holds its own immutable Roslyn Solution snapshot pair (OriginalSolution
        // + ModifiedSolution), so a sibling `*_apply` that bumped the workspace version does
        // NOT invalidate this token. Below we rebase the preview's INTENDED diff
        // (`ModifiedSolution.GetChanges(OriginalSolution)`) onto the CURRENT workspace
        // solution via `RebaseModifiedSolutionOntoCurrent` — edits to files the sibling
        // didn't touch replay cleanly; edits that collide with a sibling's edits win
        // last-write semantics. If the underlying workspace was closed or reloaded, the
        // IPreviewStore has already dropped the token via its lifecycle hook and Retrieve
        // above returned null.

        // Item #4 — severity-high-output-would-ship-as-is-and-fail-code and the
        // "preview truncated while apply still mutates disk" concern. The agent reviewing
        // the preview could not see all the changes the apply will make; refusing the
        // blind apply by default makes the corruption path explicit. Callers that deliberately
        // want to apply without full visibility pass `force: true` in the apply tool schema.
        if (diffTruncated && !force)
        {
            return TruncatedPreviewResult();
        }

        var currentSolution = _workspace.GetCurrentSolution(workspaceId);
        var preparation = await PrepareApplyAsync(
            originalSolution,
            modifiedSolution,
            currentSolution,
            ct).ConfigureAwait(false);
        _undoService?.CaptureBeforeApply(
            workspaceId,
            description,
            currentSolution,
            preparation.FileSnapshots);

        var (success, appliedFiles) = await _documentSetPersistence.PersistAsync(
            workspaceId,
            currentSolution,
            preparation.ModifiedSolution,
            preparation.SolutionChanges,
            ct).ConfigureAwait(false);

        _previewStore.Invalidate(previewToken);

        if (!success)
        {
            _postApplyResolver?.Invalidate(previewToken);
            return new ApplyResultDto(false, [], "Failed to apply changes to the workspace.");
        }

        _changeTracker?.RecordChange(workspaceId, description, appliedFiles, toolName);
        _logger.LogInformation("Applied refactoring '{Description}' to {Count} file(s)", description, appliedFiles.Count);

        var mutatedSymbol = await ResolvePostApplySymbolAsync(
            workspaceId,
            previewToken,
            ct).ConfigureAwait(false);
        return new ApplyResultDto(true, appliedFiles, null, mutatedSymbol);
    }

    private static ApplyResultDto InvalidPreviewResult() =>
        new(
            false,
            [],
            "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");

    private static ApplyResultDto TruncatedPreviewResult() =>
        new(
            false,
            [],
            "Refusing to apply a truncated preview — the diff was capped for payload-size reasons so the reviewed preview is not a complete picture of the disk state the apply will produce. " +
            "Options: (1) re-run the preview with a narrower scope (smaller file set or more targeted symbol) to fit under the diff cap; " +
            "(2) if you understand the tradeoff and want to proceed without full visibility, call the apply tool again with `force: true`.");

    private async Task<RefactoringApplyPreparation> PrepareApplyAsync(
        Solution originalSolution,
        Solution modifiedSolution,
        Solution currentSolution,
        CancellationToken ct)
    {
        // Rebase onto the current solution so an unrelated sibling apply is preserved.
        var rebasedSolution = await RebaseModifiedSolutionOntoCurrentAsync(
            originalSolution,
            modifiedSolution,
            currentSolution,
            ct).ConfigureAwait(false);
        var solutionChanges = rebasedSolution.GetChanges(currentSolution);
        var hasFileSetChanges = solutionChanges.GetProjectChanges().Any(projectChange =>
            projectChange.GetAddedDocuments().Any()
            || projectChange.GetRemovedDocuments().Any()
            || projectChange.GetAddedProjectReferences().Any()
            || projectChange.GetRemovedProjectReferences().Any());
        var fileSnapshots = hasFileSetChanges
            ? await BuildFileSnapshotsForSolutionChangesAsync(
                currentSolution,
                rebasedSolution,
                solutionChanges,
                ct).ConfigureAwait(false)
            : null;

        return new RefactoringApplyPreparation(rebasedSolution, solutionChanges, fileSnapshots);
    }

    private async Task<SymbolDto?> ResolvePostApplySymbolAsync(
        string workspaceId,
        string previewToken,
        CancellationToken ct)
    {
        // Rename is the only apply that rotates symbol identity. Other apply kinds retain the
        // pre-apply handle, so no resolver entry exists for them.
        if (_postApplyResolver is null)
        {
            return null;
        }

        var appliedSolution = _workspace.GetCurrentSolution(workspaceId);
        return await _postApplyResolver
            .ConsumeAsync(previewToken, appliedSolution, ct)
            .ConfigureAwait(false);
    }

    private sealed record RefactoringApplyPreparation(
        Solution ModifiedSolution,
        SolutionChanges SolutionChanges,
        IReadOnlyList<FileSnapshotDto>? FileSnapshots);

    /// <summary>
    /// Previews removing unnecessary usings and organizing import directives.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        // organize-usings-preview-document-not-found-after-apply — route through the shared
        // DocumentResolution helper so this tool and format_document_preview both re-acquire
        // the same post-auto-reload snapshot from IWorkspaceManager and raise identical
        // InvalidOperationException("Document not found: ...") messages when the file is not
        // part of the workspace session.
        var (solution, document) = DocumentResolution.GetDocumentFromFreshSolutionOrThrow(
            _workspace, workspaceId, filePath);

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax root for '{filePath}'.");
        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax tree for '{filePath}'.");
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not compile project for '{filePath}'.");

        var unnecessaryUsings = compilation.GetDiagnostics(ct)
            .Where(diagnostic => diagnostic.Id == "CS8019" && diagnostic.Location.SourceTree == syntaxTree)
            .Select(diagnostic => root.FindNode(diagnostic.Location.SourceSpan))
            .OfType<UsingDirectiveSyntax>()
            .Distinct()
            .ToList();

        if (unnecessaryUsings.Count > 0)
        {
            root = root.RemoveNodes(unnecessaryUsings, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
            if (root is CompilationUnitSyntax cu)
            {
                cu = TriviaNormalizationHelper.NormalizeLeadingTrivia(cu);
                cu = TriviaNormalizationHelper.CollapseBlankLinesInUsingBlock(cu);
                root = cu;
            }
            document = document.WithSyntaxRoot(root);
        }

        var organizedDoc = await Formatter.OrganizeImportsAsync(document, ct).ConfigureAwait(false);
        var newSolution = organizedDoc.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Organize usings in '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.OrganizeUsings);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    /// <summary>
    /// Previews formatting an entire document using Roslyn formatting rules.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        // organize-usings-preview-document-not-found-after-apply — shared resolver; see sibling
        // PreviewOrganizeUsingsAsync note.
        var (solution, document) = DocumentResolution.GetDocumentFromFreshSolutionOrThrow(
            _workspace, workspaceId, filePath);

        var formattedDoc = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
        var newSolution = formattedDoc.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Format document '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.FormatDocument);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewFormatRangeAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct)
    {
        // organize-usings-preview-document-not-found-after-apply — shared resolver.
        var (solution, document) = DocumentResolution.GetDocumentFromFreshSolutionOrThrow(
            _workspace, workspaceId, filePath);

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        ValidateFormatRange(text, startLine, startColumn, endLine, endColumn);
        var rangedText = await FormatRangeTextAsync(document, text, startLine, endLine, ct).ConfigureAwait(false);
        var newSolution = rangedText.ContentEquals(text)
            ? solution
            : document.WithText(rangedText).Project.Solution;

        return await CreateFormatRangePreviewAsync(
            workspaceId, filePath, startLine, endLine, solution, newSolution, ct).ConfigureAwait(false);
    }

    private async Task<RefactoringPreviewDto> CreateFormatRangePreviewAsync(
        string workspaceId,
        string filePath,
        int startLine,
        int endLine,
        Solution originalSolution,
        Solution newSolution,
        CancellationToken ct)
    {
        var changes = await SolutionDiffHelper.ComputeChangesAsync(originalSolution, newSolution, ct).ConfigureAwait(false);
        var description = $"Format range in '{Path.GetFileName(filePath)}' (lines {startLine}-{endLine})";
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            PreviewKind.FormatRange);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static void ValidateFormatRange(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        ValidatePositiveFormatCoordinates(startLine, startColumn, endLine, endColumn);
        ValidateFormatLineBounds(text, startLine, endLine);
        ValidateFormatRangeOrdering(startLine, startColumn, endLine, endColumn);
    }

    private static void ValidatePositiveFormatCoordinates(
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        if (startLine < 1) throw new ArgumentException($"startLine must be >= 1 (got {startLine}).", nameof(startLine));
        if (endLine < 1) throw new ArgumentException($"endLine must be >= 1 (got {endLine}).", nameof(endLine));
        if (startColumn < 1) throw new ArgumentException($"startColumn must be >= 1 (got {startColumn}).", nameof(startColumn));
        if (endColumn < 1) throw new ArgumentException($"endColumn must be >= 1 (got {endColumn}).", nameof(endColumn));
    }

    private static void ValidateFormatLineBounds(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startLine,
        int endLine)
    {
        if (startLine > text.Lines.Count)
            throw new ArgumentException($"startLine ({startLine}) is past the end of the file ({text.Lines.Count} lines).", nameof(startLine));
        if (endLine > text.Lines.Count)
            throw new ArgumentException($"endLine ({endLine}) is past the end of the file ({text.Lines.Count} lines).", nameof(endLine));
    }

    private static void ValidateFormatRangeOrdering(
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        if (endLine < startLine)
            throw new ArgumentException($"endLine ({endLine}) must be >= startLine ({startLine}).", nameof(endLine));
        if (startLine == endLine && startColumn > endColumn)
            throw new ArgumentException($"startColumn ({startColumn}) must be <= endColumn ({endColumn}) when both are on the same line.", nameof(startColumn));
    }

    private static async Task<Microsoft.CodeAnalysis.Text.SourceText> FormatRangeTextAsync(
        Document document,
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startLine,
        int endLine,
        CancellationToken ct)
    {
        // format-range-preview-empty-diff-compile-check-filter-false-clean +
        // dr-9-12-flag-format-range-empty-returns-empty-diff-on-d:
        //
        // Previously this called `Formatter.FormatAsync(document, [span], …)`, which
        // silently dropped formatting edits whose target trivia sat outside the
        // explicit span. Result: `format_range_preview` returned a `unifiedDiff` with
        // headers and no `@@` hunks while a subsequent `format_range_apply` shared the
        // same stored (no-op) solution — the empty preview led callers to believe
        // nothing would change, then attribute any observed disk mutation to bugs
        // elsewhere in the apply pipeline.
        //
        // Fix: format the whole document, then construct a "ranged" output by
        // splicing — keep the formatter's text for lines inside [startLine, endLine]
        // and the caller's original text outside. The formatter has full context
        // (no boundary truncation) and the splice guarantees the apply path only
        // touches lines the caller asked for. Whatever the splice produces is what
        // the preview's `unifiedDiff` reports and what the apply will write to disk.
        //
        // Sub-line column precision (startColumn/endColumn) is not threaded into the
        // splice: the formatter's edits are line-anchored, so a column-precise splice
        // would re-introduce the boundary-trivia drop bug this fix removes. The
        // caller's column inputs are still validated above so existing failure-mode
        // contracts (out-of-range column, inverted range) keep working.
        var formattedDoc = await Formatter.FormatAsync(document, options: null, cancellationToken: ct).ConfigureAwait(false);
        var formattedText = await formattedDoc.GetTextAsync(ct).ConfigureAwait(false);

        var rangedText = SpliceFormattedRange(text, formattedText, startLine, endLine);

        // dr-9-7-only-partially-normalizes-whitespace: Formatter.FormatAsync re-indents and
        // normalizes inter-token whitespace, and the splice picks up its trailing-whitespace
        // strip on rewritten lines, but neither pass collapses runs of consecutive blank
        // lines (3+ newlines in a row → 2+ blank lines). That's a separate
        // normalization which Roslyn's trivia formatter doesn't perform, so we do it
        // post-splice — only inside the caller's requested range so out-of-range blank-line
        // patterns stay untouched.
        return CollapseBlankLineRunsInRange(rangedText, startLine, endLine);
    }

    public async Task<RefactoringPreviewDto> PreviewCodeFixAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        string? fixId,
        CancellationToken ct)
    {
        // organize-usings-preview-document-not-found-after-apply — shared resolver.
        var (solution, document) = DocumentResolution.GetDocumentFromFreshSolutionOrThrow(
            _workspace, workspaceId, filePath);

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax tree for '{filePath}'.");

        // code-fix-providers-missing-ca: locate the diagnostic via the registry-aware path so we
        // can match analyzer diagnostics (CA*/IDE*) too, not only compiler diagnostics. Falls
        // back to compiler-only when no registry is wired (legacy callers / unit tests).
        var diagnostic = await FindDiagnosticAtPositionAsync(
            document, syntaxTree, diagnosticId, line, column, ct).ConfigureAwait(false);

        if (diagnostic is null)
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnosticId}' was not found at {filePath}:{line}:{column}. " +
                "Run project_diagnostics first and copy an exact (id, line, column) tuple from a real entry.");
        }

        // Try the provider registry first — covers CA*/IDE*/SCS* and any third-party analyzers.
        var provider = _codeFixRegistry?.FirstProviderFor(diagnosticId, solution);
        if (provider is not null)
        {
            var registeredAction = await CaptureFirstActionAsync(provider, document, diagnostic, fixId, ct)
                .ConfigureAwait(false);
            if (registeredAction is not null)
            {
                var operations = await registeredAction.GetOperationsAsync(ct).ConfigureAwait(false);
                var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
                if (applyOp is not null)
                {
                    var newSol = applyOp.ChangedSolution;
                    var diff = await SolutionDiffHelper.ComputeChangesAsync(solution, newSol, ct).ConfigureAwait(false);
                    var actionId = registeredAction.EquivalenceKey ?? registeredAction.Title ?? provider.GetType().Name;
                    var desc = $"Apply code fix '{actionId}' for {diagnosticId} in '{Path.GetFileName(filePath)}'";
                    var tk = _previewStore.Store(workspaceId, newSol, _workspace.GetCurrentVersion(workspaceId), desc, diff, PreviewKind.CodeFix);
                    return new RefactoringPreviewDto(tk, desc, diff, null);
                }
            }
        }

        // Fallback: the legacy CS8019 / remove_unused_using path stays for callers that do not
        // wire a CodeFixProviderRegistry (notably some unit tests). Anything else now produces
        // a clearer error than the historic "no supported curated code fix" message.
        var normalizedFixId = string.IsNullOrWhiteSpace(fixId) ? GetDefaultFixId(diagnosticId) : fixId;
        if (string.Equals(diagnosticId, "CS8019", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedFixId, "remove_unused_using", StringComparison.OrdinalIgnoreCase))
        {
            return await PreviewRemoveUnusedUsingFallbackAsync(
                workspaceId, solution, document, syntaxTree, diagnostic, normalizedFixId, ct)
                .ConfigureAwait(false);
        }

        // code-fix-preview-vs-fix-all-preview-shape-inconsistency — mirror FixAllService:78-87's
        // structured empty envelope when no provider is registered. Previously this branch threw
        // InvalidOperationException, forcing callers to catch a generic exception to discover the
        // same "no provider loaded" condition that fix_all_preview returns as data. Returning an
        // envelope with empty PreviewToken/Changes + a non-null GuidanceMessage keeps the two
        // tools' shape consistent. Reuses FixAllService.BuildNoProviderGuidance so the guidance
        // text (suppression / severity-bump hints + id-specific tool pointers) stays unified.
        return new RefactoringPreviewDto(
            PreviewToken: string.Empty,
            Description: $"No code fix provider for '{diagnosticId}'.",
            Changes: Array.Empty<FileChangeDto>(),
            Warnings: null,
            CallsiteUpdates: null,
            GuidanceMessage: FixAllService.BuildNoProviderGuidance(diagnosticId));
    }

    private async Task<RefactoringPreviewDto> PreviewRemoveUnusedUsingFallbackAsync(
        string workspaceId, Solution solution, Document document, SyntaxTree syntaxTree,
        Diagnostic diagnostic, string normalizedFixId, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax root for '{document.FilePath}'.");

        var usingDirective = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>()
            ?? root.FindNode(diagnostic.Location.SourceSpan) as UsingDirectiveSyntax;
        if (usingDirective is null)
        {
            throw new InvalidOperationException("The unused using directive could not be resolved.");
        }

        var newRoot = root.RemoveNode(usingDirective, SyntaxRemoveOptions.KeepExteriorTrivia)
            ?? throw new InvalidOperationException("Failed to remove the unused using directive.");
        var newSolution = document.WithSyntaxRoot(newRoot).Project.Solution;
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Apply code fix '{normalizedFixId}' for CS8019 in '{Path.GetFileName(document.FilePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.CodeFix);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    /// <summary>
    /// Locates a diagnostic at the requested position using compiler diagnostics first
    /// (cheapest), then falling back to the analyzer pipeline when the diagnostic id starts
    /// with a non-CS prefix. Avoids running analyzers when callers asked for a CS* diagnostic.
    /// </summary>
    private static async Task<Diagnostic?> FindDiagnosticAtPositionAsync(
        Document document, SyntaxTree syntaxTree, string diagnosticId, int line, int column, CancellationToken ct)
    {
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not compile project for '{document.FilePath}'.");

        bool MatchesPosition(Diagnostic candidate) =>
            string.Equals(candidate.Id, diagnosticId, StringComparison.OrdinalIgnoreCase) &&
            candidate.Location.IsInSource &&
            candidate.Location.SourceTree == syntaxTree &&
            candidate.Location.GetLineSpan().StartLinePosition.Line + 1 == line &&
            candidate.Location.GetLineSpan().StartLinePosition.Character + 1 == column;

        var compilerHit = compilation.GetDiagnostics(ct).FirstOrDefault(MatchesPosition);
        if (compilerHit is not null) return compilerHit;

        // Only run analyzers when the id is non-CS; the GetAnalyzerDiagnosticsAsync path is
        // expensive on large projects and we already know CS* ids are compiler-only.
        if (diagnosticId.StartsWith("CS", StringComparison.OrdinalIgnoreCase)) return null;

        var analyzers = document.Project.AnalyzerReferences
            .SelectMany(r => r.GetAnalyzers(document.Project.Language))
            .Where(a => a.SupportedDiagnostics.Any(d =>
                string.Equals(d.Id, diagnosticId, StringComparison.OrdinalIgnoreCase)))
            .ToImmutableArray();
        if (analyzers.IsEmpty) return null;

        var withAnalyzers = compilation.WithAnalyzers(analyzers);
        var analyzerDiags = await withAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
        return analyzerDiags.FirstOrDefault(MatchesPosition);
    }

    /// <summary>
    /// Invokes <paramref name="provider"/> for the given <paramref name="diagnostic"/> and
    /// returns the first <see cref="CodeAction"/> registered. When <paramref name="fixId"/>
    /// is supplied, prefers the action whose <see cref="CodeAction.EquivalenceKey"/> matches.
    /// </summary>
    private static async Task<CodeAction?> CaptureFirstActionAsync(
        CodeFixProvider provider, Document document, Diagnostic diagnostic, string? fixId, CancellationToken ct)
    {
        CodeAction? first = null;
        CodeAction? matchingFixId = null;

        var context = new CodeFixContext(document, diagnostic, (action, _) =>
        {
            first ??= action;
            if (matchingFixId is null && fixId is not null &&
                string.Equals(action.EquivalenceKey, fixId, StringComparison.Ordinal))
            {
                matchingFixId = action;
            }
        }, ct);

        try
        {
            await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return matchingFixId ?? first;
    }

    /// <summary>
    /// preview-token-cross-coupling-bundle (BREAKING): replays the preview's intended diff
    /// onto the CURRENT workspace solution. The preview's intent is captured as
    /// <c>modifiedSolution.GetChanges(originalSolution)</c>; this helper reapplies only
    /// those changes on top of <paramref name="currentSolution"/> so sibling <c>*_apply</c>
    /// calls that mutated unrelated files since the preview was stored are preserved.
    /// Collisions on the same document fall to last-apply-wins text semantics. Added
    /// and removed documents are mirrored into the rebased solution; the caller's existing
    /// document-set-change code path (PersistDocumentSetChangesAsync) then handles I/O.
    /// </summary>
    private static async Task<Solution> RebaseModifiedSolutionOntoCurrentAsync(
        Solution originalSolution,
        Solution modifiedSolution,
        Solution currentSolution,
        CancellationToken ct)
    {
        // Short-circuit: nothing moved between originalSolution and currentSolution — the
        // legacy in-lineage path is still optimal.
        if (ReferenceEquals(originalSolution, currentSolution))
        {
            return modifiedSolution;
        }

        var previewChanges = modifiedSolution.GetChanges(originalSolution);
        var rebased = currentSolution;
        var currentDocumentsByPath = BuildCurrentDocumentPathIndex(currentSolution);

        foreach (var projectChange in previewChanges.GetProjectChanges())
        {
            rebased = await RebaseChangedDocumentsAsync(
                rebased,
                modifiedSolution,
                projectChange,
                currentDocumentsByPath,
                ct).ConfigureAwait(false);
            rebased = await RebaseAddedDocumentsAsync(
                rebased,
                modifiedSolution,
                projectChange,
                ct).ConfigureAwait(false);
            rebased = RebaseRemovedDocuments(rebased, originalSolution, projectChange);
            rebased = RebaseProjectReferences(
                rebased,
                originalSolution,
                modifiedSolution,
                projectChange);
        }

        return rebased;
    }

    private static IReadOnlyDictionary<string, DocumentId> BuildCurrentDocumentPathIndex(
        Solution solution)
    {
        var documentsByPath = new Dictionary<string, DocumentId>(FileSystemPath.Comparer);
        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            if (!string.IsNullOrWhiteSpace(document.FilePath))
            {
                documentsByPath.TryAdd(document.FilePath, document.Id);
            }
        }

        return documentsByPath;
    }

    private static async Task<Solution> RebaseChangedDocumentsAsync(
        Solution rebased,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        IReadOnlyDictionary<string, DocumentId> currentDocumentsByPath,
        CancellationToken ct)
    {
        foreach (var documentId in projectChange.GetChangedDocuments())
        {
            var modifiedDocument = modifiedSolution.GetDocument(documentId);
            if (modifiedDocument is null)
            {
                continue;
            }

            var currentDocument = rebased.GetDocument(documentId);
            if (currentDocument is null
                && !string.IsNullOrWhiteSpace(modifiedDocument.FilePath)
                && currentDocumentsByPath.TryGetValue(modifiedDocument.FilePath, out var matchedDocumentId))
            {
                currentDocument = rebased.GetDocument(matchedDocumentId);
            }

            if (currentDocument is not null)
            {
                var sourceText = await modifiedDocument.GetTextAsync(ct).ConfigureAwait(false);
                rebased = rebased.WithDocumentText(currentDocument.Id, sourceText);
            }
        }

        return rebased;
    }

    private static async Task<Solution> RebaseAddedDocumentsAsync(
        Solution rebased,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        CancellationToken ct)
    {
        foreach (var documentId in projectChange.GetAddedDocuments())
        {
            var document = modifiedSolution.GetDocument(documentId);
            if (document?.FilePath is null)
            {
                continue;
            }

            var targetProject = ResolveRebaseTargetProject(rebased, projectChange.ProjectId, document.Project);
            if (targetProject is null)
            {
                continue;
            }

            var sourceText = await document.GetTextAsync(ct).ConfigureAwait(false);
            var newDocumentId = DocumentId.CreateNewId(targetProject.Id, document.Name);
            rebased = rebased.AddDocument(
                newDocumentId,
                document.Name,
                sourceText,
                document.Folders,
                document.FilePath);
        }

        return rebased;
    }

    private static Project? ResolveRebaseTargetProject(
        Solution rebased,
        ProjectId originalProjectId,
        Project modifiedProject)
    {
        var directMatch = rebased.GetProject(originalProjectId);
        if (directMatch is not null || modifiedProject.FilePath is null)
        {
            return directMatch;
        }

        return rebased.Projects.FirstOrDefault(project =>
            string.Equals(
                project.FilePath,
                modifiedProject.FilePath,
                FileSystemPath.Comparison));
    }

    private static Solution RebaseRemovedDocuments(
        Solution rebased,
        Solution originalSolution,
        ProjectChanges projectChange)
    {
        foreach (var documentId in projectChange.GetRemovedDocuments())
        {
            var filePath = originalSolution.GetDocument(documentId)?.FilePath;
            if (filePath is null)
            {
                continue;
            }

            var target = rebased.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.FilePath, filePath, FileSystemPath.Comparison));
            if (target is not null)
            {
                rebased = rebased.RemoveDocument(target.Id);
            }
        }

        return rebased;
    }

    private static Solution RebaseProjectReferences(
        Solution rebased,
        Solution originalSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange)
    {
        var modifiedProject = modifiedSolution.GetProject(projectChange.ProjectId);
        var targetProject = modifiedProject is null
            ? rebased.GetProject(projectChange.ProjectId)
            : ResolveRebaseTargetProject(rebased, projectChange.ProjectId, modifiedProject);
        if (targetProject is null)
        {
            return rebased;
        }

        foreach (var projectReference in projectChange.GetAddedProjectReferences())
        {
            var referencedProject = modifiedSolution.GetProject(projectReference.ProjectId);
            var rebasedReference = referencedProject is null
                ? rebased.GetProject(projectReference.ProjectId)
                : ResolveRebaseTargetProject(rebased, projectReference.ProjectId, referencedProject);
            if (rebasedReference is not null
                && !targetProject.ProjectReferences.Any(reference =>
                    reference.ProjectId == rebasedReference.Id))
            {
                rebased = rebased.AddProjectReference(
                    targetProject.Id,
                    new ProjectReference(rebasedReference.Id, projectReference.Aliases, projectReference.EmbedInteropTypes));
                targetProject = rebased.GetProject(targetProject.Id)!;
            }
        }

        foreach (var projectReference in projectChange.GetRemovedProjectReferences())
        {
            var referencedProject = originalSolution.GetProject(projectReference.ProjectId);
            var rebasedReference = referencedProject is null
                ? rebased.GetProject(projectReference.ProjectId)
                : ResolveRebaseTargetProject(rebased, projectReference.ProjectId, referencedProject);
            var existingReference = targetProject.ProjectReferences.FirstOrDefault(reference =>
                reference.ProjectId == rebasedReference?.Id);
            if (existingReference is not null)
            {
                rebased = rebased.RemoveProjectReference(targetProject.Id, existingReference);
                targetProject = rebased.GetProject(targetProject.Id)!;
            }
        }

        return rebased;
    }

    /// <summary>
    /// Item #2 — build the authoritative FileSnapshotDto list that <see cref="UndoService"/>'s
    /// fast path uses to restore disk state. Added documents get <c>OriginalText: null</c>
    /// (delete-on-revert); removed documents get the pre-apply disk text (recreate-on-revert);
    /// changed documents get the pre-apply disk text (overwrite-on-revert).
    /// </summary>
    private static async Task<IReadOnlyList<FileSnapshotDto>> BuildFileSnapshotsForSolutionChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        var snapshots = new List<FileSnapshotDto>();
        var seenPaths = new HashSet<string>(FileSystemPath.Comparer);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetAddedDocuments())
            {
                await AddFileSnapshotAsync(
                    snapshots,
                    seenPaths,
                    modifiedSolution.GetDocument(documentId),
                    missingFileIsNew: true,
                    ct).ConfigureAwait(false);
            }

            foreach (var documentId in projectChange.GetRemovedDocuments())
            {
                await AddFileSnapshotAsync(
                    snapshots,
                    seenPaths,
                    currentSolution.GetDocument(documentId),
                    missingFileIsNew: false,
                    ct).ConfigureAwait(false);
            }

            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                await AddFileSnapshotAsync(
                    snapshots,
                    seenPaths,
                    currentSolution.GetDocument(documentId),
                    missingFileIsNew: false,
                    ct).ConfigureAwait(false);
            }

            if (projectChange.GetAddedProjectReferences().Any()
                || projectChange.GetRemovedProjectReferences().Any())
            {
                await AddProjectFileSnapshotAsync(
                    snapshots,
                    seenPaths,
                    currentSolution.GetProject(projectChange.ProjectId)
                        ?? modifiedSolution.GetProject(projectChange.ProjectId),
                    ct).ConfigureAwait(false);
            }
        }

        return snapshots;
    }

    private static async Task AddFileSnapshotAsync(
        ICollection<FileSnapshotDto> snapshots,
        ISet<string> seenPaths,
        Document? document,
        bool missingFileIsNew,
        CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        var filePath = document.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !seenPaths.Add(filePath))
        {
            return;
        }

        var existingBytes = File.Exists(filePath)
            ? await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false)
            : null;

        // Only the missing-file case has a fallback to compute, and only when the file is not
        // brand new. GetTextAsync stays inside this branch deliberately: materializing the whole
        // SourceText is wasted work on the common file-exists path.
        string? fallbackText = null;
        if (existingBytes is null && !missingFileIsNew)
        {
            fallbackText = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
        }

        snapshots.Add(FileSnapshotCapture.FromBytesOrFallback(filePath, existingBytes, fallbackText));
    }

    private static async Task AddProjectFileSnapshotAsync(
        ICollection<FileSnapshotDto> snapshots,
        ISet<string> seenPaths,
        Project? project,
        CancellationToken ct)
    {
        var projectPath = project?.FilePath;
        if (string.IsNullOrWhiteSpace(projectPath)
            || !seenPaths.Add(projectPath)
            || !File.Exists(projectPath))
        {
            return;
        }

        snapshots.Add(FileSnapshotCapture.FromBytesOrFallback(
            projectPath,
            await File.ReadAllBytesAsync(projectPath, ct).ConfigureAwait(false),
            fallbackText: null));
    }

    private static string GetDefaultFixId(string diagnosticId) =>
        diagnosticId switch
        {
            "CS8019" => "remove_unused_using",
            _ => string.Empty
        };

    /// <summary>
    /// Splices the formatter's output for the requested line range back into the original
    /// text. Lines outside [<paramref name="startLine"/>, <paramref name="endLine"/>] (1-based,
    /// inclusive) come from <paramref name="originalText"/>; lines inside come from the
    /// formatter's whole-document output, mapped from the original anchor line.
    ///
    /// Why splice rather than apply <c>Formatter.FormatAsync(doc, [span])</c>: that overload
    /// silently drops formatting edits whose target trivia falls outside the explicit span
    /// (see <c>format-range-preview-empty-diff-compile-check-filter-false-clean</c>). Whole-
    /// document formatting + line-splice gives the formatter full context AND keeps the apply
    /// scope honest to what the caller asked for.
    ///
    /// Line correspondence is anchored at <c>startLine - 1</c>. A formatter result with a
    /// different line count cannot be mapped without risking edits outside the requested
    /// range, so this method explicitly refuses that result.
    /// </summary>
    internal static Microsoft.CodeAnalysis.Text.SourceText SpliceFormattedRange(
        Microsoft.CodeAnalysis.Text.SourceText originalText,
        Microsoft.CodeAnalysis.Text.SourceText formattedText,
        int startLine,
        int endLine)
    {
        var (startIdx, endIdx) = NormalizeSpliceRange(originalText, startLine, endLine);
        if (endIdx < startIdx)
        {
            return originalText;
        }

        if (originalText.Lines.Count != formattedText.Lines.Count)
        {
            throw new InvalidOperationException(
                $"Format-range preview refused a formatter result that changed the line count " +
                $"from {originalText.Lines.Count} to {formattedText.Lines.Count}; the requested range " +
                $"cannot be kept bounded safely.");
        }

        return SpliceMatchingLineCounts(originalText, formattedText, startIdx, endIdx);
    }

    private static (int StartIdx, int EndIdx) NormalizeSpliceRange(
        Microsoft.CodeAnalysis.Text.SourceText originalText,
        int startLine,
        int endLine)
    {
        var startIdx = startLine - 1;
        var endIdx = endLine - 1;
        if (startIdx < 0)
        {
            startIdx = 0;
        }
        if (endIdx >= originalText.Lines.Count)
        {
            endIdx = originalText.Lines.Count - 1;
        }
        return (startIdx, endIdx);
    }

    private static Microsoft.CodeAnalysis.Text.SourceText SpliceMatchingLineCounts(
        Microsoft.CodeAnalysis.Text.SourceText originalText,
        Microsoft.CodeAnalysis.Text.SourceText formattedText,
        int startIdx,
        int endIdx)
    {
        var builder = new System.Text.StringBuilder(originalText.Length);
        for (var lineIndex = 0; lineIndex < originalText.Lines.Count; lineIndex++)
        {
            var useFormattedLine = lineIndex >= startIdx && lineIndex <= endIdx;
            var sourceText = useFormattedLine ? formattedText : originalText;
            AppendLineAndBreak(builder, sourceText, sourceText.Lines[lineIndex]);
        }

        return Microsoft.CodeAnalysis.Text.SourceText.From(
            builder.ToString(),
            originalText.Encoding,
            originalText.ChecksumAlgorithm);
    }

    /// <summary>
    /// Collapses runs of two or more consecutive blank lines to a single blank line,
    /// but only when the entire run sits inside the caller's requested range
    /// [<paramref name="startLine"/>, <paramref name="endLine"/>] (1-based, inclusive).
    /// A "blank line" here is one whose <see cref="Microsoft.CodeAnalysis.Text.TextLine.ToString"/>
    /// is empty or pure whitespace — the splice already strips trailing whitespace via
    /// the formatter, so blank-by-whitespace and blank-by-emptiness collapse identically.
    ///
    /// Why this exists: Roslyn's <c>Formatter.FormatAsync</c> normalizes indentation and
    /// inter-token whitespace, but does not collapse multi-blank-line runs — that's
    /// usually the job of an analyzer + code-fix pair (e.g. IDE0303 family) which we
    /// don't run here. <c>format_range_preview</c> is contracted to deliver
    /// "Roslyn-style whitespace cleanup over the requested range," and three blank lines
    /// where one belongs is a whitespace anomaly the caller expects fixed (audit
    /// dr-9-7-only-partially-normalizes-whitespace).
    ///
    /// Out-of-range blank-line runs are preserved verbatim. A run that crosses the range
    /// boundary is also preserved — collapsing it would silently mutate text outside the
    /// caller's selection, which the splice contract forbids.
    /// </summary>
    private static Microsoft.CodeAnalysis.Text.SourceText CollapseBlankLineRunsInRange(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startLine,
        int endLine)
    {
        var (startIdx, endIdx) = NormalizeCollapsedBlankLineRange(text, startLine, endLine);
        if (endIdx < startIdx)
        {
            return text;
        }
        var dropIndices = CollectBlankLineIndicesToDrop(text, startIdx, endIdx);
        if (dropIndices.Count == 0)
        {
            return text;
        }

        return BuildCollapsedSourceText(text, dropIndices);
    }

    private static (int StartIdx, int EndIdx) NormalizeCollapsedBlankLineRange(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startLine,
        int endLine)
    {
        var startIdx = System.Math.Max(0, startLine - 1);
        var endIdx = System.Math.Min(text.Lines.Count - 1, endLine - 1);
        return (startIdx, endIdx);
    }

    private static System.Collections.Generic.HashSet<int> CollectBlankLineIndicesToDrop(
        Microsoft.CodeAnalysis.Text.SourceText text,
        int startIdx,
        int endIdx)
    {
        var dropIndices = new System.Collections.Generic.HashSet<int>();
        var lineIndex = 0;
        while (lineIndex < text.Lines.Count)
        {
            if (!IsBlankLine(text.Lines[lineIndex]))
            {
                lineIndex++;
                continue;
            }

            var runEnd = lineIndex;
            while (runEnd + 1 < text.Lines.Count && IsBlankLine(text.Lines[runEnd + 1]))
            {
                runEnd++;
            }

            if (lineIndex >= startIdx && runEnd <= endIdx)
            {
                for (var dropIndex = lineIndex + 1; dropIndex <= runEnd; dropIndex++)
                {
                    dropIndices.Add(dropIndex);
                }
            }

            lineIndex = runEnd + 1;
        }

        return dropIndices;
    }

    private static Microsoft.CodeAnalysis.Text.SourceText BuildCollapsedSourceText(
        Microsoft.CodeAnalysis.Text.SourceText text,
        System.Collections.Generic.HashSet<int> dropIndices)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (var lineIndex = 0; lineIndex < text.Lines.Count; lineIndex++)
        {
            if (dropIndices.Contains(lineIndex))
            {
                continue;
            }

            AppendLineAndBreak(sb, text, text.Lines[lineIndex]);
        }

        return Microsoft.CodeAnalysis.Text.SourceText.From(sb.ToString(), text.Encoding, text.ChecksumAlgorithm);
    }

    private static void AppendLineAndBreak(
        System.Text.StringBuilder sb,
        Microsoft.CodeAnalysis.Text.SourceText text,
        Microsoft.CodeAnalysis.Text.TextLine line)
    {
        sb.Append(line.ToString());
        var lineBreakStart = line.End;
        var lineBreakEnd = line.EndIncludingLineBreak;
        if (lineBreakEnd <= lineBreakStart)
        {
            return;
        }

        var breakSpan = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(lineBreakStart, lineBreakEnd);
        sb.Append(text.GetSubText(breakSpan).ToString());
    }

    private static bool IsBlankLine(Microsoft.CodeAnalysis.Text.TextLine line)
    {
        var s = line.ToString();
        for (var n = 0; n < s.Length; n++)
        {
            if (s[n] != ' ' && s[n] != '\t')
            {
                return false;
            }
        }

        return true;
    }

}
