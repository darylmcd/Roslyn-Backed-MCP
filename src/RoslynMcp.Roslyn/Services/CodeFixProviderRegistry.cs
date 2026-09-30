using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Resolves <see cref="CodeFixProvider"/> instances for a given diagnostic id by combining
/// providers loaded from the IDE Features assembly (one-time, cached) with providers loaded
/// from each project's analyzer references (lazy per reference identity).
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
    private readonly Func<AnalyzerFileReference, FeatureProviderLoadResult<CodeFixProvider>> _analyzerProviderLoader;

    /// <summary>
    /// Cache by reference identity: the reference owns its loader and load context. Weak keys
    /// let retired references and their providers leave with the old workspace snapshot.
    /// </summary>
    private readonly ConditionalWeakTable<AnalyzerFileReference, Lazy<FeatureProviderLoadResult<CodeFixProvider>>>
        _byReference = new();

    public CodeFixProviderRegistry(
        ILogger<CodeFixProviderRegistry> logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        : this((ILogger)logger, exceptionReporter)
    {
    }

    internal CodeFixProviderRegistry(
        ILogger logger,
        IUnexpectedExceptionReporter? exceptionReporter)
        : this(
            logger,
            () => CSharpFeatureProviderLoader.Load<CodeFixProvider>(logger, exceptionReporter),
            reference => CSharpFeatureProviderLoader.LoadFromAssemblyFactory<CodeFixProvider>(
                reference.GetAssembly,
                logger,
                exceptionReporter,
                exportAttributeFullName: typeof(ExportCodeFixProviderAttribute).FullName))
    {
    }

    internal CodeFixProviderRegistry(
        ILogger logger,
        Func<FeatureProviderLoadResult<CodeFixProvider>> staticProviderLoader,
        Func<AnalyzerFileReference, FeatureProviderLoadResult<CodeFixProvider>> analyzerProviderLoader)
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
        var seenReferencesByPath = new Dictionary<string, HashSet<AnalyzerFileReference>>(
            FileSystemPath.Comparer);
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.AnalyzerReferences)
            {
                if (reference is not AnalyzerFileReference fileRef) continue;
                var path = fileRef.FullPath;
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!seenReferencesByPath.TryGetValue(path, out var seenReferences))
                {
                    seenReferences = new HashSet<AnalyzerFileReference>(ReferenceEqualityComparer.Instance);
                    seenReferencesByPath.Add(path, seenReferences);
                }

                if (!seenReferences.Add(fileRef)) continue;

                // Preserve the reference's dependency resolver and isolated load context.
                var providers = _byReference.GetValue(fileRef, reference =>
                    new Lazy<FeatureProviderLoadResult<CodeFixProvider>>(
                        () => _analyzerProviderLoader(reference)));
                yield return providers.Value;
            }
        }
    }
}
