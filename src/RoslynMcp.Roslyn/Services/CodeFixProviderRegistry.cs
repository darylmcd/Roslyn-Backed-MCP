using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

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
    private readonly Lazy<FeatureProviderLoadResult<CodeFixProvider>> _staticProviders;
    private readonly Func<string, FeatureProviderLoadResult<CodeFixProvider>> _analyzerProviderLoader;

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
        : this(
            logger,
            () => CSharpFeatureProviderLoader.Load<CodeFixProvider>(logger, exceptionReporter),
            analyzerPath => CSharpFeatureProviderLoader.LoadFromAssemblyFactory<CodeFixProvider>(
                () => Assembly.LoadFrom(analyzerPath),
                logger,
                exceptionReporter))
    {
    }

    internal CodeFixProviderRegistry(
        ILogger<CodeFixProviderRegistry> logger,
        Func<FeatureProviderLoadResult<CodeFixProvider>> staticProviderLoader,
        Func<string, FeatureProviderLoadResult<CodeFixProvider>> analyzerProviderLoader)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(staticProviderLoader);
        ArgumentNullException.ThrowIfNull(analyzerProviderLoader);

        _staticProviders = new Lazy<FeatureProviderLoadResult<CodeFixProvider>>(staticProviderLoader);
        _analyzerProviderLoader = analyzerProviderLoader;
    }

    public CodeFixProviderLookupResult GetProvidersForDetailed(
        string diagnosticId,
        Solution? solution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticId);

        var staticResult = _staticProviders.Value;
        var results = staticResult.Providers
            .Where(provider => provider.FixableDiagnosticIds.Contains(diagnosticId))
            .ToList();
        var failedProviderCount = staticResult.FailedProviderCount;
        var loadedProviderCount = staticResult.Providers.Length;

        if (solution is not null)
        {
            foreach (var loadResult in EnumerateProjectProviderResults(solution))
            {
                failedProviderCount += loadResult.FailedProviderCount;
                loadedProviderCount += loadResult.Providers.Length;
                results.AddRange(loadResult.Providers.Where(provider =>
                    provider.FixableDiagnosticIds.Contains(diagnosticId)));
            }
        }

        return new CodeFixProviderLookupResult(
            results,
            IsComplete: failedProviderCount == 0,
            FailedProviderCount: failedProviderCount,
            LoadedProviderCount: loadedProviderCount);
    }

    /// <summary>
    /// Returns every <see cref="CodeFixProvider"/> known to the registry that supports
    /// <paramref name="diagnosticId"/>. Includes providers loaded from the IDE Features
    /// assembly and any project analyzer assemblies in <paramref name="solution"/>.
    /// </summary>
    public IReadOnlyList<CodeFixProvider> GetProvidersFor(string diagnosticId, Solution? solution = null)
    {
        var result = GetProvidersForDetailed(diagnosticId, solution);
        if (!result.IsComplete && result.LoadedProviderCount == 0)
        {
            throw new InvalidOperationException(
                $"Code fix provider discovery for '{diagnosticId}' was incomplete; " +
                $"{result.FailedProviderCount} provider load(s) failed.");
        }

        return result.Providers;
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
        _analyzerProviderLoader(analyzerPath);
}
