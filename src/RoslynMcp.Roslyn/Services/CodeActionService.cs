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

namespace RoslynMcp.Roslyn.Services;

public sealed class CodeActionService : ICodeActionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<CodeActionService> _logger;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;
    private readonly Lazy<FeatureProviderLoadResult<CodeFixProvider>> _codeFixProviders;
    private readonly Lazy<FeatureProviderLoadResult<CodeRefactoringProvider>> _codeRefactoringProviders;

    public CodeActionService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ILogger<CodeActionService> logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
        _exceptionReporter = exceptionReporter;
        _codeFixProviders = new Lazy<FeatureProviderLoadResult<CodeFixProvider>>(
            () => CSharpFeatureProviderLoader.Load<CodeFixProvider>(_logger, _exceptionReporter));
        _codeRefactoringProviders = new Lazy<FeatureProviderLoadResult<CodeRefactoringProvider>>(
            () => CSharpFeatureProviderLoader.Load<CodeRefactoringProvider>(_logger, _exceptionReporter));
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

        var flattenedFixActions = FlattenActions(fixActions).ToList();
        var flattenedRefactoringActions = FlattenActions(refactoringActions).ToList();

        var dtos = new List<CodeActionDto>(flattenedFixActions.Count + flattenedRefactoringActions.Count);
        var index = 0;
        foreach (var action in flattenedFixActions)
        {
            dtos.Add(new CodeActionDto(
                Index: index++,
                Title: action.Title,
                Kind: ResolveKind(action.Action, defaultKind: "CodeFix"),
                EquivalenceKey: action.Action.EquivalenceKey));
        }
        foreach (var action in flattenedRefactoringActions)
        {
            dtos.Add(new CodeActionDto(
                Index: index++,
                Title: action.Title,
                Kind: ResolveKind(action.Action, defaultKind: "Refactoring"),
                EquivalenceKey: action.Action.EquivalenceKey));
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

        var fixActions = new List<CodeAction>();
        var refactoringActions = new List<CodeAction>();

        await CollectCodeFixesAsync(document, span, fixActions, ct).ConfigureAwait(false);
        await CollectRefactoringsAsync(document, span, refactoringActions, ct).ConfigureAwait(false);

        var actions = FlattenActions(fixActions)
            .Concat(FlattenActions(refactoringActions))
            .ToList();

        if (actionIndex < 0 || actionIndex >= actions.Count)
            throw new ArgumentException($"Action index {actionIndex} is out of range. Available actions: {actions.Count}");

        var selectedAction = actions[actionIndex];
        var operations = await selectedAction.Action.GetOperationsAsync(ct).ConfigureAwait(false);
        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

        if (applyOp is null)
            throw new InvalidOperationException($"Code action '{selectedAction.Title}' does not produce workspace changes.");

        var newSolution = applyOp.ChangedSolution;
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Code action: {selectedAction.Title}";
        // preview-token-apply-route-provenance: tag the token with its producer family so
        // apply_code_action can refuse a token minted elsewhere before mutating.
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            kind: PreviewKind.CodeAction);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static IEnumerable<ActionCandidate> FlattenActions(IEnumerable<CodeAction> actions, string? parentTitle = null)
    {
        foreach (var action in actions)
        {
            var title = parentTitle is null ? action.Title : $"{parentTitle}: {action.Title}";
            var nestedActions = action.NestedActions;
            if (!nestedActions.IsDefaultOrEmpty)
            {
                foreach (var nestedAction in FlattenActions(nestedActions, title))
                {
                    yield return nestedAction;
                }

                continue;
            }

            yield return new ActionCandidate(action, title);
        }
    }

    private sealed record ActionCandidate(CodeAction Action, string Title);

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

        foreach (var provider in _codeFixProviders.Value.Providers)
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
                    var details = UnexpectedExceptionReporting.Report(
                        _exceptionReporter,
                        ex,
                        UnexpectedExceptionCategory.AnalysisScan);
                    _logger.LogDebug(
                        "Code fix provider {Provider} failed with {ExceptionType}; correlationId={CorrelationId}",
                        provider.GetType().Name,
                        details.Server.ExceptionTypes.FirstOrDefault() ?? "unknown",
                        details.Public.CorrelationId);
                }
            }
        }
    }

    private async Task CollectRefactoringsAsync(Document document, TextSpan span, List<CodeAction> actions, CancellationToken ct)
    {
        foreach (var provider in _codeRefactoringProviders.Value.Providers)
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
                var details = UnexpectedExceptionReporting.Report(
                    _exceptionReporter,
                    ex,
                    UnexpectedExceptionCategory.AnalysisScan);
                _logger.LogDebug(
                    "Code refactoring provider {Provider} failed with {ExceptionType}; correlationId={CorrelationId}",
                    provider.GetType().Name,
                    details.Server.ExceptionTypes.FirstOrDefault() ?? "unknown",
                    details.Public.CorrelationId);
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

        // get-code-actions-caret-only-inverted-range: When caret-only callers pass a startColumn
        // past the line's last character (common on callers that supply a column >= the line
        // length, e.g. a caret logically sitting at EOL on a short line), the original code
        // built TextSpan.FromBounds(startPosition, lineEnd) with lineEnd < startPosition,
        // yielding an inverted-range ArgumentOutOfRangeException. Clamp end >= start so a
        // caret-only call always yields a well-formed span — a zero-width selection at the
        // caret when startColumn is past EOL, otherwise the remainder of the line.
        var lineEnd = text.Lines[startLine - 1].End;
        return TextSpan.FromBounds(startPosition, Math.Max(startPosition, lineEnd));
    }

}
