using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Resolves <see cref="CodeFixProvider"/> instances for a given diagnostic id by combining
/// providers loaded from the IDE Features assembly (one-time, cached) with providers loaded
/// from each project's analyzer references (lazy per analyzer-assembly path).
///
/// This is shared by <see cref="RefactoringService.PreviewCodeFixAsync"/> and
/// <see cref="FixAllService.PreviewFixAllAsync"/> so a single source of truth tracks which
/// curated fixes are available for any diagnostic id.
///
/// Implements <see cref="ICodeFixProviderRegistry"/>.
/// </summary>
public sealed class CodeFixProviderRegistry : ICodeFixProviderRegistry
{
    private readonly ILogger<CodeFixProviderRegistry> _logger;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;
    private readonly Lazy<FeatureProviderLoadResult<CodeFixProvider>> _staticProviders;

    /// <summary>
    /// Cache of providers loaded from individual analyzer assembly paths. Many projects share
    /// the same analyzer assembly (e.g. Microsoft.CodeAnalysis.NetAnalyzers), so caching by
    /// path avoids re-reflecting on every PreviewCodeFix call.
    /// </summary>
    private readonly ConcurrentDictionary<string, FeatureProviderLoadResult<CodeFixProvider>> _byAssemblyPath
        = new(StringComparer.OrdinalIgnoreCase);

    public CodeFixProviderRegistry(
        ILogger<CodeFixProviderRegistry> logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _logger = logger;
        _exceptionReporter = exceptionReporter;
        _staticProviders = new Lazy<FeatureProviderLoadResult<CodeFixProvider>>(
            () => CSharpFeatureProviderLoader.Load<CodeFixProvider>(_logger, _exceptionReporter));
    }

    /// <summary>
    /// Returns every <see cref="CodeFixProvider"/> known to the registry that supports
    /// <paramref name="diagnosticId"/>. Includes providers loaded from the IDE Features
    /// assembly and any project analyzer assemblies in <paramref name="solution"/>.
    /// </summary>
    public IReadOnlyList<CodeFixProvider> GetProvidersFor(string diagnosticId, Solution? solution = null)
    {
        var staticResult = _staticProviders.Value;
        var results = staticResult.Providers
            .Where(provider => provider.FixableDiagnosticIds.Contains(diagnosticId))
            .ToList();

        if (solution is not null)
        {
            foreach (var loadResult in EnumerateProjectProviderResults(solution))
            {
                results.AddRange(loadResult.Providers.Where(provider =>
                    provider.FixableDiagnosticIds.Contains(diagnosticId)));
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the first provider that supports <paramref name="diagnosticId"/>, or null when
    /// none are available. Convenience for single-provider call sites.
    /// </summary>
    public CodeFixProvider? FirstProviderFor(string diagnosticId, Solution? solution = null) =>
        GetProvidersFor(diagnosticId, solution).FirstOrDefault();

    private IEnumerable<FeatureProviderLoadResult<CodeFixProvider>> EnumerateProjectProviderResults(Solution solution)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.AnalyzerReferences)
            {
                if (reference is not AnalyzerFileReference fileRef) continue;
                var path = fileRef.Display;
                if (string.IsNullOrWhiteSpace(path) || !seenPaths.Add(path)) continue;

                var providers = _byAssemblyPath.GetOrAdd(path, LoadProvidersFromAssembly);
                yield return providers;
            }
        }
    }

    private FeatureProviderLoadResult<CodeFixProvider> LoadProvidersFromAssembly(string analyzerPath) =>
        CSharpFeatureProviderLoader.LoadFromAssemblyFactory<CodeFixProvider>(
            () => Assembly.LoadFrom(analyzerPath),
            _logger,
            _exceptionReporter);
}
