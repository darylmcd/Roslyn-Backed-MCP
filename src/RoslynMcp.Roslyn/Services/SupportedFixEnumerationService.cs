using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Enumerates code-fix providers and actions for one diagnostic while preserving healthy
/// partial results, cancellation, and secret-safe provider-failure diagnostics.
/// </summary>
internal sealed class SupportedFixEnumerationService
{
    private static readonly Action<ILogger, string, string, string, string, Exception?> LogProviderEnumerationFailed =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Debug,
            new EventId(1, nameof(LogProviderEnumerationFailed)),
            "Code fix provider {Provider} failed during diagnostic enumeration with {ExceptionType}; category={Category}; correlationId={CorrelationId}");

    private readonly ICodeFixProviderRegistry _codeFixRegistry;
    private readonly ILogger _logger;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;

    public SupportedFixEnumerationService(
        ICodeFixProviderRegistry codeFixRegistry,
        ILogger logger,
        IUnexpectedExceptionReporter? exceptionReporter)
    {
        _codeFixRegistry = codeFixRegistry;
        _logger = logger;
        _exceptionReporter = exceptionReporter;
    }

    public async Task<SupportedFixEnumerationResult> EnumerateAsync(
        Diagnostic diagnostic,
        Solution solution,
        string filePath,
        CancellationToken ct)
    {
        var providerLookup = _codeFixRegistry.GetProvidersForDetailed(diagnostic.Id, solution);
        var providers = providerLookup.Providers;
        if (providers.Count == 0)
        {
            return new SupportedFixEnumerationResult(
                [],
                providerLookup.IsComplete,
                providerLookup.FailedProviderCount);
        }

        var document = ResolveDocument(diagnostic, solution, filePath);
        if (document is null)
        {
            return new SupportedFixEnumerationResult(
                ProjectProvidersToFixOptions(providers, diagnostic.Id),
                providerLookup.IsComplete,
                providerLookup.FailedProviderCount);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<CodeFixOptionDto>();
        var failedProviderCount = providerLookup.FailedProviderCount;
        foreach (var provider in providers)
        {
            var actionEnumeration = await CaptureRegisteredActionsAsync(
                provider,
                document,
                diagnostic,
                ct).ConfigureAwait(false);
            failedProviderCount += actionEnumeration.FailedProviderCount;
            foreach (var action in actionEnumeration.Actions)
            {
                var fixId = action.EquivalenceKey ?? action.Title ?? provider.GetType().Name;
                if (!seen.Add(fixId))
                {
                    continue;
                }

                results.Add(new CodeFixOptionDto(
                    FixId: fixId,
                    Title: action.Title ?? fixId,
                    Description: BuildFixDescription(provider, action)));
            }
        }

        return new SupportedFixEnumerationResult(
            results,
            IsComplete: providerLookup.IsComplete && failedProviderCount == 0,
            FailedProviderCount: failedProviderCount);
    }

    public static string? GetGuidance(
        string diagnosticId,
        SupportedFixEnumerationResult fixEnumeration)
    {
        if (!fixEnumeration.IsComplete)
        {
            return $"Code fix enumeration for '{diagnosticId}' was incomplete because " +
                   $"{fixEnumeration.FailedProviderCount} provider(s) failed. Any listed fixes are partial. " +
                   "Use get_code_actions at the diagnostic location to retry IDE-supplied actions, " +
                   "and use the correlation id from server diagnostics when investigating repeated failures.";
        }

        if (fixEnumeration.Fixes.Count > 0)
        {
            return null;
        }

        return $"No code fix provider is currently loaded for '{diagnosticId}'. " +
               "Use get_code_actions at the diagnostic location to list IDE-supplied actions, " +
               "then preview_code_action to apply one. If you expected an analyzer-supplied fix, " +
               "verify the analyzer NuGet package is restored and listed in list_analyzers.";
    }

    private static Document? ResolveDocument(Diagnostic diagnostic, Solution solution, string filePath)
    {
        if (diagnostic.Location.IsInSource && diagnostic.Location.SourceTree is { } sourceTree)
        {
            var sourceDocument = solution.GetDocument(sourceTree);
            if (sourceDocument is not null)
            {
                return sourceDocument;
            }
        }

        var documentIds = solution.GetDocumentIdsWithFilePath(filePath);
        return documentIds.IsDefaultOrEmpty ? null : solution.GetDocument(documentIds[0]);
    }

    private static IReadOnlyList<CodeFixOptionDto> ProjectProvidersToFixOptions(
        IReadOnlyList<CodeFixProvider> providers,
        string diagnosticId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<CodeFixOptionDto>();
        foreach (var provider in providers)
        {
            var providerName = provider.GetType().Name;
            if (!seen.Add(providerName))
            {
                continue;
            }

            results.Add(new CodeFixOptionDto(
                FixId: providerName,
                Title: providerName,
                Description: $"Code fix provider for {diagnosticId} (loaded from {provider.GetType().Assembly.GetName().Name}). " +
                             "No source document was available to enumerate specific fix actions; " +
                             "use get_code_actions at the diagnostic location for the actionable list."));
        }

        return results;
    }

    private static string BuildFixDescription(CodeFixProvider provider, CodeAction action)
    {
        var assemblyName = provider.GetType().Assembly.GetName().Name ?? "unknown";
        return $"Code fix \"{action.Title}\" registered by {provider.GetType().Name} ({assemblyName}).";
    }

    private async Task<CodeFixActionEnumerationResult> CaptureRegisteredActionsAsync(
        CodeFixProvider provider,
        Document document,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var captured = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => captured.Add(action), ct);

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
            LogProviderEnumerationFailed(
                _logger,
                provider.GetType().Name,
                details.Server.ExceptionTypes.FirstOrDefault() ?? "unknown",
                UnexpectedExceptionCategory.AnalysisScan.ToString(),
                details.Public.CorrelationId,
                null);
            return new CodeFixActionEnumerationResult([], FailedProviderCount: 1);
        }

        return new CodeFixActionEnumerationResult(captured, FailedProviderCount: 0);
    }

    private sealed record CodeFixActionEnumerationResult(
        IReadOnlyList<CodeAction> Actions,
        int FailedProviderCount);
}

internal sealed record SupportedFixEnumerationResult(
    IReadOnlyList<CodeFixOptionDto> Fixes,
    bool IsComplete,
    int FailedProviderCount);
