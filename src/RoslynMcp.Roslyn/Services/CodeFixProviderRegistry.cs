using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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
    private readonly Lazy<ImmutableArray<CodeFixProvider>> _staticProviders;

    /// <summary>
    /// Cache of providers loaded from individual analyzer assembly paths. Many projects share
    /// the same analyzer assembly (e.g. Microsoft.CodeAnalysis.NetAnalyzers), so caching by
    /// path avoids re-reflecting on every PreviewCodeFix call.
    /// </summary>
    private readonly ConcurrentDictionary<string, ImmutableArray<CodeFixProvider>> _byAssemblyPath
        = new(StringComparer.OrdinalIgnoreCase);

    public CodeFixProviderRegistry(ILogger<CodeFixProviderRegistry> logger)
    {
        _logger = logger;
        _staticProviders = new Lazy<ImmutableArray<CodeFixProvider>>(LoadStaticProviders);
    }

    /// <summary>
    /// Returns every <see cref="CodeFixProvider"/> known to the registry that supports
    /// <paramref name="diagnosticId"/>. Includes providers loaded from the IDE Features
    /// assembly and any project analyzer assemblies in <paramref name="solution"/>.
    /// </summary>
    public IReadOnlyList<CodeFixProvider> GetProvidersFor(string diagnosticId, Solution? solution = null)
    {
        var results = new List<CodeFixProvider>();

        foreach (var provider in _staticProviders.Value)
        {
            if (provider.FixableDiagnosticIds.Contains(diagnosticId))
                results.Add(provider);
        }

        if (solution is not null)
        {
            foreach (var provider in EnumerateProjectProviders(solution))
            {
                if (provider.FixableDiagnosticIds.Contains(diagnosticId))
                    results.Add(provider);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the first provider that supports <paramref name="diagnosticId"/>, or null when
    /// none are available. Convenience for single-provider call sites.
    /// </summary>
    public CodeFixProvider? FirstProviderFor(string diagnosticId, Solution? solution = null)
    {
        foreach (var provider in _staticProviders.Value)
        {
            if (provider.FixableDiagnosticIds.Contains(diagnosticId))
                return provider;
        }

        if (solution is not null)
        {
            foreach (var provider in EnumerateProjectProviders(solution))
            {
                if (provider.FixableDiagnosticIds.Contains(diagnosticId))
                    return provider;
            }
        }

        return null;
    }

    private IEnumerable<CodeFixProvider> EnumerateProjectProviders(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.AnalyzerReferences)
            {
                if (reference is not AnalyzerFileReference fileRef) continue;
                var path = fileRef.Display;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var providers = _byAssemblyPath.GetOrAdd(path, LoadProvidersFromAssembly);
                foreach (var provider in providers)
                    yield return provider;
            }
        }
    }

    private ImmutableArray<CodeFixProvider> LoadProvidersFromAssembly(string analyzerPath)
    {
        try
        {
            var assembly = Assembly.LoadFrom(analyzerPath);
            var providers = new List<CodeFixProvider>();
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                try
                {
                    if (Activator.CreateInstance(type) is CodeFixProvider provider)
                        providers.Add(provider);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex,
                        "CodeFixProviderRegistry: could not instantiate {Type} from {Path}",
                        type.FullName, analyzerPath);
                }
            }
            return [..providers];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "CodeFixProviderRegistry: could not load code fix providers from analyzer assembly {Path}",
                analyzerPath);
            return [];
        }
    }

    private ImmutableArray<CodeFixProvider> LoadStaticProviders()
    {
        try
        {
            var assembly = Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
            var candidates = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
                .ToList();

            var providers = candidates
                .Select(t =>
                {
                    try { return (CodeFixProvider?)Activator.CreateInstance(t); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
                })
                .Where(p => p is not null)
                .Cast<CodeFixProvider>()
                .ToImmutableArray();

            var skipped = candidates.Count - providers.Length;
            _logger.LogInformation(
                "CodeFixProviderRegistry: loaded {Loaded} providers from CSharp.Features ({Skipped} skipped — no parameterless constructor)",
                providers.Length, skipped);
            return providers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CodeFixProviderRegistry: failed to load IDE Features providers");
            return [];
        }
    }
}
