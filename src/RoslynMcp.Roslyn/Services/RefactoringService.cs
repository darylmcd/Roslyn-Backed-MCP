using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Xml.Linq;

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
    private readonly ILogger<RefactoringService> _logger;

    public RefactoringService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<RefactoringService> logger, IUndoService? undoService = null, IChangeTracker? changeTracker = null, ICodeFixProviderRegistry? codeFixRegistry = null)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _undoService = undoService;
        _changeTracker = changeTracker;
        _codeFixRegistry = codeFixRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Previews renaming a symbol and all its references across the solution.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewRenameAsync(
        string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
            throw new InvalidOperationException("No symbol found for the provided rename target.");

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

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Rename '{symbol.Name}' to '{newName}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

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
    /// Applies a previously previewed refactoring. Validates the preview token against the current
    /// workspace version to reject stale changes.
    /// </summary>
    public async Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct)
    {
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(
                false, [],
                "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, modifiedSolution, workspaceVersion, description) = entry.Value;
        if (_workspace.GetCurrentVersion(workspaceId) != workspaceVersion)
        {
            _previewStore.Invalidate(previewToken);
            return new ApplyResultDto(
                false,
                [],
                "Preview token is stale because the target workspace changed. Please create a new preview.");
        }

        var currentSolution = _workspace.GetCurrentSolution(workspaceId);
        _undoService?.CaptureBeforeApply(workspaceId, description, currentSolution);
        var solutionChanges = modifiedSolution.GetChanges(currentSolution);
        var hasDocumentSetChanges = solutionChanges.GetProjectChanges()
            .Any(projectChange => projectChange.GetAddedDocuments().Any() || projectChange.GetRemovedDocuments().Any());

        bool success;
        IReadOnlyList<string> appliedFiles;
        if (hasDocumentSetChanges)
        {
            (success, appliedFiles) = await PersistDocumentSetChangesAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                solutionChanges,
                ct).ConfigureAwait(false);
        }
        else
        {
            success = _workspace.TryApplyChanges(workspaceId, modifiedSolution);
            appliedFiles = solutionChanges.GetProjectChanges()
                .SelectMany(projectChange => projectChange.GetChangedDocuments())
                .Select(documentId => modifiedSolution.GetDocument(documentId)?.FilePath)
                .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // BUG-N1: MSBuildWorkspace.TryApplyChanges updates the in-memory solution but does not
            // reliably persist text edits to disk for change-only operations. Write every changed
            // document explicitly (same as PersistDocumentSetChangesAsync changed-doc loop).
            if (success)
            {
                try
                {
                    await PersistChangedDocumentsFromSolutionAsync(modifiedSolution, solutionChanges, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Failed to persist changed documents to disk for workspace {WorkspaceId}", workspaceId);
                    return new ApplyResultDto(false, [], "Failed to persist applied changes to disk.");
                }
            }
        }

        _previewStore.Invalidate(previewToken);

        if (!success)
        {
            return new ApplyResultDto(false, [], "Failed to apply changes to the workspace.");
        }

        _changeTracker?.RecordChange(workspaceId, description, appliedFiles, "refactoring_apply");
        _logger.LogInformation("Applied refactoring '{Description}' to {Count} file(s)", description, appliedFiles.Count);
        return new ApplyResultDto(true, appliedFiles, null);
    }

    /// <summary>
    /// Previews removing unnecessary usings and organizing import directives.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

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
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    /// <summary>
    /// Previews formatting an entire document using Roslyn formatting rules.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var formattedDoc = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
        var newSolution = formattedDoc.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Format document '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewFormatRangeAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        // format-range-preview-nonfunctional: validate parameters upfront so callers get a
        // structured error instead of an uninterpreted ArgumentOutOfRangeException from
        // TextSpan.FromBounds(-1, …) or text.Lines[-1].
        if (startLine < 1) throw new ArgumentException($"startLine must be >= 1 (got {startLine}).", nameof(startLine));
        if (endLine < 1) throw new ArgumentException($"endLine must be >= 1 (got {endLine}).", nameof(endLine));
        if (startColumn < 1) throw new ArgumentException($"startColumn must be >= 1 (got {startColumn}).", nameof(startColumn));
        if (endColumn < 1) throw new ArgumentException($"endColumn must be >= 1 (got {endColumn}).", nameof(endColumn));
        if (endLine < startLine)
            throw new ArgumentException($"endLine ({endLine}) must be >= startLine ({startLine}).", nameof(endLine));
        if (startLine > text.Lines.Count)
            throw new ArgumentException($"startLine ({startLine}) is past the end of the file ({text.Lines.Count} lines).", nameof(startLine));
        if (endLine > text.Lines.Count)
            throw new ArgumentException($"endLine ({endLine}) is past the end of the file ({text.Lines.Count} lines).", nameof(endLine));
        if (startLine == endLine && startColumn > endColumn)
            throw new ArgumentException($"startColumn ({startColumn}) must be <= endColumn ({endColumn}) when both are on the same line.", nameof(startColumn));

        var startPosition = text.Lines[startLine - 1].Start + (startColumn - 1);
        var endPosition = text.Lines[endLine - 1].Start + (endColumn - 1);
        // Clamp endPosition to the end of the file so a column past the end of the last line
        // doesn't cause TextSpan.FromBounds to throw.
        if (endPosition > text.Length) endPosition = text.Length;
        if (startPosition > endPosition) startPosition = endPosition;

        var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startPosition, endPosition);

        // format-range-preview-nonfunctional: pass an explicit IEnumerable<TextSpan> overload to
        // avoid a runtime overload-binding ambiguity between
        // FormatAsync(Document, TextSpan, OptionSet?, CancellationToken) and the newer
        // FormatAsync(Document, TextSpan, SyntaxFormattingOptions?, CancellationToken).
        Microsoft.CodeAnalysis.Text.TextSpan[] spans = [span];
        var formattedDoc = await Formatter.FormatAsync(document, spans, options: null, cancellationToken: ct).ConfigureAwait(false);
        var newSolution = formattedDoc.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Format range in '{Path.GetFileName(filePath)}' (lines {startLine}-{endLine})";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
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
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
        {
            throw new InvalidOperationException($"Document not found: {filePath}");
        }

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
                    var tk = _previewStore.Store(workspaceId, newSol, _workspace.GetCurrentVersion(workspaceId), desc);
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

        throw new InvalidOperationException(
            $"No code fix provider is loaded for diagnostic '{diagnosticId}'. " +
            "Run list_analyzers to see which analyzers are loaded, or restore analyzer NuGet packages.");
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
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

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

    private static async Task PersistChangedDocumentsFromSolutionAsync(
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = modifiedSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<(bool Success, IReadOnlyList<string> AppliedFiles)> PersistDocumentSetChangesAsync(
        string workspaceId,
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        var appliedFiles = new List<string>();

        // Item #5 — severity-medium-breaks-msbuild-until-csproj-is-hand.
        //
        // MSBuildWorkspace.TryApplyChanges, when it sees an added document in an
        // SDK-style csproj with default Compile globbing enabled, injects an explicit
        // <Compile Include="…"/> item into the csproj XML. Because the SDK's default
        // glob already matches every .cs under the project directory, the injection
        // produces a "Duplicate 'Compile' items were included" MSBuild error on the
        // next workspace_reload (firewall-analyzer audit §9.6 BUG-COMPILE-INCLUDE;
        // IT-Chat-Bot audit §9.1).
        //
        // We capture which added-document projects are SDK-style here so that after
        // the TryApplyChanges call we can restore each affected csproj from its
        // pre-apply bytes — undoing Roslyn's injection while keeping the in-memory
        // workspace's view of the added document (TryApplyChanges has already added
        // the document to the in-memory Solution). The next reload picks up the new
        // file through the SDK's default glob, so the on-disk csproj stays clean.
        var sdkProjectCsprojSnapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var projectChange in solutionChanges.GetProjectChanges())
            {
                await PersistProjectReferenceChangesAsync(currentSolution, modifiedSolution, projectChange, appliedFiles, ct).ConfigureAwait(false);

                // Snapshot csproj BEFORE we write any new files (the csproj itself is
                // untouched on disk at this point; capture its canonical bytes).
                var addedDocuments = projectChange.GetAddedDocuments().ToList();
                if (addedDocuments.Count > 0)
                {
                    var project = modifiedSolution.GetProject(projectChange.ProjectId);
                    if (project?.FilePath is not null
                        && ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(project.FilePath, _logger)
                        && !sdkProjectCsprojSnapshots.ContainsKey(project.FilePath))
                    {
                        var csprojBytes = await File.ReadAllTextAsync(project.FilePath, ct).ConfigureAwait(false);
                        sdkProjectCsprojSnapshots[project.FilePath] = csprojBytes;
                    }
                }

                foreach (var documentId in addedDocuments)
                {
                    var document = modifiedSolution.GetDocument(documentId);
                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    var directory = Path.GetDirectoryName(document.FilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                    await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
                    appliedFiles.Add(document.FilePath);
                }

                foreach (var documentId in projectChange.GetChangedDocuments())
                {
                    var document = modifiedSolution.GetDocument(documentId);
                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                    await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
                    appliedFiles.Add(document.FilePath);
                }

                foreach (var documentId in projectChange.GetRemovedDocuments())
                {
                    var document = currentSolution.GetDocument(documentId);
                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    if (File.Exists(document.FilePath))
                    {
                        File.Delete(document.FilePath);
                    }

                    appliedFiles.Add(document.FilePath);
                }
            }

            // scaffold-type-apply-perf: previously every document add/remove triggered a full
            // workspace reload (~10 s on Jellyfin). Try the in-memory TryApplyChanges path
            // first — MSBuildWorkspace supports added/removed/changed documents — and only
            // fall back to ReloadAsync if the workspace rejects the change. TryApplyChanges
            // bumps WorkspaceSession.Version so per-version caches invalidate correctly.
            var applied = _workspace.TryApplyChanges(workspaceId, modifiedSolution);

            // Item #5 — re-apply csproj snapshots for SDK-style projects, undoing
            // any <Compile Include=…/> injection TryApplyChanges wrote to disk. This
            // runs whether TryApplyChanges succeeded or failed: on failure we'll
            // ReloadAsync immediately after, and having the csproj back to its
            // original bytes ensures the reloaded in-memory workspace matches the
            // pre-apply csproj (the new file is discovered via the SDK glob).
            foreach (var (csprojPath, originalContent) in sdkProjectCsprojSnapshots)
            {
                try
                {
                    var currentContent = await File.ReadAllTextAsync(csprojPath, ct).ConfigureAwait(false);
                    if (!string.Equals(currentContent, originalContent, StringComparison.Ordinal))
                    {
                        await File.WriteAllTextAsync(csprojPath, originalContent, ct).ConfigureAwait(false);
                        _logger.LogDebug(
                            "Item #5: restored SDK-style csproj {Path} after TryApplyChanges injected an explicit <Compile> item (default glob will pick up the new file on next reload).",
                            csprojPath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Rare on the apply path since we just wrote sibling files in the same
                    // directory, but a restore failure here does not invalidate the apply
                    // itself — the duplicate-Compile error surfaces only on the NEXT reload,
                    // which the caller will see clearly. Log and continue.
                    _logger.LogWarning(ex,
                        "Item #5: failed to restore SDK-style csproj snapshot for {Path}; the project may show a duplicate-<Compile> build error until manually edited.",
                        csprojPath);
                }
            }

            if (!applied)
            {
                _logger.LogInformation(
                    "TryApplyChanges rejected document-set changes for {WorkspaceId}; falling back to full ReloadAsync.",
                    workspaceId);
                await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            }
            return (true, appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to persist document set changes for workspace {WorkspaceId}", workspaceId);
            return (false, []);
        }
    }

    private static async Task PersistProjectReferenceChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        var modifiedProject = modifiedSolution.GetProject(projectChange.ProjectId);
        if (modifiedProject?.FilePath is null || !File.Exists(modifiedProject.FilePath))
        {
            return;
        }

        var addedProjectReferences = projectChange.GetAddedProjectReferences().ToArray();
        var removedProjectReferences = projectChange.GetRemovedProjectReferences().ToArray();
        if (addedProjectReferences.Length == 0 && removedProjectReferences.Length == 0)
        {
            return;
        }

        var originalContent = await File.ReadAllTextAsync(modifiedProject.FilePath, ct).ConfigureAwait(false);
        var document = XDocument.Parse(originalContent, LoadOptions.PreserveWhitespace);
        var projectDirectory = Path.GetDirectoryName(modifiedProject.FilePath)
            ?? throw new InvalidOperationException("Project file path must have a parent directory.");
        var changed = false;

        foreach (var projectReference in addedProjectReferences)
        {
            var referencedProject = modifiedSolution.GetProject(projectReference.ProjectId);
            if (referencedProject?.FilePath is null)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, referencedProject.FilePath);
            if (document.Descendants("ProjectReference").Any(element =>
                    string.Equals(NormalizeInclude((string?)element.Attribute("Include")), NormalizeInclude(relativePath), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            GetOrCreateItemGroup(document, "ProjectReference")
                .Add(new XElement("ProjectReference", new XAttribute("Include", relativePath)));
            changed = true;
        }

        foreach (var projectReference in removedProjectReferences)
        {
            var referencedProject = currentSolution.GetProject(projectReference.ProjectId) ?? modifiedSolution.GetProject(projectReference.ProjectId);
            var targetFileName = Path.GetFileName(referencedProject?.FilePath);
            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                continue;
            }

            var element = document.Descendants("ProjectReference").FirstOrDefault(candidate =>
            {
                var include = (string?)candidate.Attribute("Include");
                return !string.IsNullOrWhiteSpace(include) &&
                       string.Equals(Path.GetFileName(include), targetFileName, StringComparison.OrdinalIgnoreCase);
            });

            if (element is null)
            {
                continue;
            }

            element.Remove();
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        await File.WriteAllTextAsync(modifiedProject.FilePath, document.ToString(SaveOptions.DisableFormatting), ct).ConfigureAwait(false);
        appliedFiles.Add(modifiedProject.FilePath);
    }

    private static XElement GetOrCreateItemGroup(XDocument document, string itemName)
    {
        var existingGroup = document.Root?.Elements("ItemGroup")
            .FirstOrDefault(group => group.Elements(itemName).Any());
        if (existingGroup is not null)
        {
            return existingGroup;
        }

        var itemGroup = new XElement("ItemGroup");
        document.Root?.Add(itemGroup);
        return itemGroup;
    }

    private static string NormalizeInclude(string? include)
    {
        return (include ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string GetDefaultFixId(string diagnosticId) =>
        diagnosticId switch
        {
            "CS8019" => "remove_unused_using",
            _ => string.Empty
        };
}
