using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Reflection;

namespace RoslynMcp.Roslyn.Services;

public sealed class CodeActionService : ICodeActionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<CodeActionService> _logger;
    private readonly Lazy<ImmutableArray<CodeFixProvider>> _codeFixProviders;
    private readonly Lazy<ImmutableArray<CodeRefactoringProvider>> _codeRefactoringProviders;

    public CodeActionService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<CodeActionService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
        _codeFixProviders = new Lazy<ImmutableArray<CodeFixProvider>>(LoadCodeFixProviders);
        _codeRefactoringProviders = new Lazy<ImmutableArray<CodeRefactoringProvider>>(LoadCodeRefactoringProviders);
    }

    public async Task<CodeActionListDto> GetCodeActionsAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, CancellationToken ct)
    {
        // dr-get-code-actions-opaque-error-on-bad-contract: Validate 1-based parameters
        // up front so callers get a clear error instead of a cryptic IndexOutOfRangeException.
        if (startLine < 1)
            throw new ArgumentException(
                $"startLine must be >= 1 (1-based). Got {startLine}. Did you pass 'line' instead of 'startLine'?",
                nameof(startLine));
        if (startColumn < 1)
            throw new ArgumentException(
                $"startColumn must be >= 1 (1-based). Got {startColumn}. Did you pass 'column' instead of 'startColumn'?",
                nameof(startColumn));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return BuildResult([]);

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var span = CreateSpan(text, startLine, startColumn, endLine, endColumn);

        // FLAG-6C: track which actions came from a code-fix provider vs a refactoring provider so
        // the Kind column reflects the real category instead of always returning "Unknown".
        var fixActions = new List<CodeAction>();
        var refactoringActions = new List<CodeAction>();

        await CollectCodeFixesAsync(document, span, fixActions, ct).ConfigureAwait(false);
        await CollectRefactoringsAsync(document, span, refactoringActions, ct).ConfigureAwait(false);

        var dtos = new List<CodeActionDto>(fixActions.Count + refactoringActions.Count);
        var index = 0;
        foreach (var action in fixActions)
        {
            dtos.Add(new CodeActionDto(
                Index: index++,
                Title: action.Title,
                Kind: ResolveKind(action, defaultKind: "CodeFix"),
                EquivalenceKey: action.EquivalenceKey));
        }
        foreach (var action in refactoringActions)
        {
            dtos.Add(new CodeActionDto(
                Index: index++,
                Title: action.Title,
                Kind: ResolveKind(action, defaultKind: "Refactoring"),
                EquivalenceKey: action.EquivalenceKey));
        }
        return BuildResult(dtos);
    }

    /// <summary>
    /// Wrap the action list with the FLAG-6B empty-result hint. The hint lives here (not
    /// in the Tool shim) so the generated MCP dispatch shim can use the ordinary
    /// ToolDispatch.ReadByWorkspaceIdAsync&lt;TDto&gt; path without custom result-shaping.
    /// Serialized JSON shape is preserved byte-identical: { count, hint, actions } in camelCase.
    /// </summary>
    private static CodeActionListDto BuildResult(IReadOnlyList<CodeActionDto> actions)
    {
        string? hint = null;
        if (actions.Count == 0)
        {
            hint = "No code fixes or refactorings were available at this position. " +
                   "Code fixes only fire when a diagnostic is reported at the span; " +
                   "refactorings typically need a wider selection (e.g. an expression or block) rather than a single caret position. " +
                   "Try widening the range with endLine/endColumn or pointing at a diagnostic flagged by project_diagnostics.";
        }
        return new CodeActionListDto(actions.Count, hint, actions);
    }

    /// <summary>
    /// FLAG-6C: Pick a meaningful Kind for a code action. Roslyn's <c>CodeAction.Tags</c> may
    /// contain semantic tags like "Refactoring" / "Style" / "Quality"; if not, fall back to the
    /// caller-provided default (CodeFix vs Refactoring derived from which provider produced it).
    /// </summary>
    private static string ResolveKind(CodeAction action, string defaultKind)
    {
        if (action.Tags.IsDefault || action.Tags.Length == 0) return defaultKind;
        foreach (var preferred in new[] { "Refactoring", "CodeFix", "Style", "Quality" })
        {
            if (action.Tags.Contains(preferred)) return preferred;
        }
        return action.Tags[0];
    }
    public async Task<RefactoringPreviewDto> PreviewCodeActionAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, int actionIndex, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var span = CreateSpan(text, startLine, startColumn, endLine, endColumn);

        var actions = new List<CodeAction>();

        await CollectCodeFixesAsync(document, span, actions, ct).ConfigureAwait(false);
        await CollectRefactoringsAsync(document, span, actions, ct).ConfigureAwait(false);

        if (actionIndex < 0 || actionIndex >= actions.Count)
            throw new ArgumentException($"Action index {actionIndex} is out of range. Available actions: {actions.Count}");

        var selectedAction = actions[actionIndex];
        var operations = await selectedAction.GetOperationsAsync(ct).ConfigureAwait(false);
        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

        if (applyOp is null)
            throw new InvalidOperationException($"Code action '{selectedAction.Title}' does not produce workspace changes.");

        var newSolution = applyOp.ChangedSolution;
        var changes = await ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Code action: {selectedAction.Title}";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private async Task CollectCodeFixesAsync(Document document, TextSpan span, List<CodeAction> actions, CancellationToken ct)
    {
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null) return;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        var diagnostics = compilation.GetDiagnostics(ct)
            .Where(d => d.Location.IsInSource &&
                        d.Location.SourceTree == syntaxTree &&
                        d.Location.SourceSpan.IntersectsWith(span))
            .ToImmutableArray();

        if (diagnostics.IsEmpty) return;

        foreach (var provider in _codeFixProviders.Value)
        {
            var fixableDiagnosticIds = provider.FixableDiagnosticIds;
            var relevantDiagnostics = diagnostics
                .Where(d => fixableDiagnosticIds.Contains(d.Id))
                .ToImmutableArray();

            if (relevantDiagnostics.IsEmpty) continue;

            foreach (var diagnostic in relevantDiagnostics)
            {
                var context = new CodeFixContext(
                    document,
                    diagnostic,
                    (action, _) => actions.Add(action),
                    ct);

                try
                {
                    await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Code fix provider {Provider} failed", provider.GetType().Name);
                }
            }
        }
    }

    private async Task CollectRefactoringsAsync(Document document, TextSpan span, List<CodeAction> actions, CancellationToken ct)
    {
        foreach (var provider in _codeRefactoringProviders.Value)
        {
            var context = new CodeRefactoringContext(
                document,
                span,
                action => actions.Add(action),
                ct);

            try
            {
                await provider.ComputeRefactoringsAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Code refactoring provider {Provider} failed", provider.GetType().Name);
            }
        }
    }

    private static TextSpan CreateSpan(SourceText text, int startLine, int startColumn, int? endLine, int? endColumn)
    {
        var startPosition = text.Lines[startLine - 1].Start + (startColumn - 1);
        if (endLine.HasValue && endColumn.HasValue)
        {
            var endPosition = text.Lines[endLine.Value - 1].Start + (endColumn.Value - 1);
            return TextSpan.FromBounds(startPosition, endPosition);
        }

        var lineEnd = text.Lines[startLine - 1].End;
        return TextSpan.FromBounds(startPosition, lineEnd);
    }

    private static Assembly? LoadCSharpFeaturesAssembly()
    {
        try
        {
            return Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private ImmutableArray<CodeFixProvider> LoadCodeFixProviders()
    {
        try
        {
            var featuresAssembly = LoadCSharpFeaturesAssembly();
            if (featuresAssembly is null)
            {
                _logger.LogWarning("Could not load Microsoft.CodeAnalysis.CSharp.Features assembly for code fix providers");
                return [];
            }

            var providers = featuresAssembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
                .Select(t =>
                {
                    try { return (CodeFixProvider?)Activator.CreateInstance(t); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
                })
                .Where(p => p is not null)
                .Cast<CodeFixProvider>()
                .ToImmutableArray();

            _logger.LogInformation("Loaded {Count} code fix providers from Microsoft.CodeAnalysis.CSharp.Features", providers.Length);
            return providers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load code fix providers");
            return [];
        }
    }

    private ImmutableArray<CodeRefactoringProvider> LoadCodeRefactoringProviders()
    {
        try
        {
            var featuresAssembly = LoadCSharpFeaturesAssembly();
            if (featuresAssembly is null)
            {
                _logger.LogWarning("Could not load Microsoft.CodeAnalysis.CSharp.Features assembly for code refactoring providers");
                return [];
            }

            var providers = featuresAssembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeRefactoringProvider).IsAssignableFrom(t))
                .Select(t =>
                {
                    try { return (CodeRefactoringProvider?)Activator.CreateInstance(t); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
                })
                .Where(p => p is not null)
                .Cast<CodeRefactoringProvider>()
                .ToImmutableArray();

            _logger.LogInformation("Loaded {Count} code refactoring providers from Microsoft.CodeAnalysis.CSharp.Features", providers.Length);
            return providers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load code refactoring providers");
            return [];
        }
    }

    private static async Task<IReadOnlyList<FileChangeDto>> ComputeChangesAsync(
        Solution oldSolution, Solution newSolution, CancellationToken ct)
    {
        var changes = new List<FileChangeDto>();
        var solutionChanges = newSolution.GetChanges(oldSolution);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = oldSolution.GetDocument(docId);
                var newDoc = newSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null) continue;

                var oldText = (await oldDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                var newText = (await newDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();

                if (oldText == newText) continue;

                var filePath = oldDoc.FilePath ?? oldDoc.Name;
                var diff = DiffGenerator.GenerateUnifiedDiff(oldText, newText, filePath);
                changes.Add(new FileChangeDto(filePath, diff));
            }
        }

        return changes;
    }
}
