using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Resolves <see cref="CodeFixProvider"/> instances for a given diagnostic id, combining the
/// IDE Features assembly's curated providers with any providers shipped via project analyzer
/// references. Used by code_fix_preview and fix_all_preview so both tools reach the same
/// providers.
/// </summary>
public interface ICodeFixProviderRegistry
{
    /// <summary>
    /// Returns every matching provider together with whether provider discovery completed, how
    /// many provider loads failed, and how many providers loaded overall. Missing parameterless
    /// constructors are intentional skips, not failures, because those providers require
    /// workspace services this registry cannot construct. The default projection preserves
    /// compatibility for external implementations that cannot report loader internals; registries
    /// with completeness data should override it.
    /// </summary>
    CodeFixProviderLookupResult GetProvidersForDetailed(
        string diagnosticId,
        Solution? solution = null)
    {
        var providers = GetProvidersFor(diagnosticId, solution);
        return new CodeFixProviderLookupResult(
            providers,
            IsComplete: true,
            FailedProviderCount: 0,
            LoadedProviderCount: providers.Count);
    }

    /// <summary>
    /// Returns every provider known to the registry that supports
    /// <paramref name="diagnosticId"/>. Pass the active <paramref name="solution"/> to
    /// include providers from the solution's analyzer references.
    /// </summary>
    IReadOnlyList<CodeFixProvider> GetProvidersFor(string diagnosticId, Solution? solution = null);

    /// <summary>
    /// Returns the first provider supporting <paramref name="diagnosticId"/>, or null when
    /// none are available. Convenience wrapper for single-provider call sites.
    /// </summary>
    CodeFixProvider? FirstProviderFor(string diagnosticId, Solution? solution = null);
}

/// <summary>
/// Completeness-aware result for code-fix provider discovery.
/// </summary>
public sealed record CodeFixProviderLookupResult(
    IReadOnlyList<CodeFixProvider> Providers,
    bool IsComplete,
    int FailedProviderCount,
    int LoadedProviderCount);
