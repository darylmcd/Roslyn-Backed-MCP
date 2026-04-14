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
