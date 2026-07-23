using System.Reflection;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Loads the <c>Microsoft.CodeAnalysis.CSharp.Features</c> assembly used to discover built-in
/// Roslyn code fix providers, code refactoring providers, and diagnostic analyzers.
/// </summary>
/// <remarks>
/// <c>dedupe-csharp-features-assembly-load-helper</c> — prior to this helper,
/// <c>CodeActionService.LoadCSharpFeaturesAssembly()</c> and
/// <c>FixAllService.LoadFeaturesAssembly()</c> each carried a byte-for-byte-identical private
/// method that loaded this assembly and swallowed any non-cancellation exception into a
/// <see langword="null"/> return. This helper centralizes that logic so both services share one
/// implementation.
/// </remarks>
internal static class CSharpFeaturesAssemblyLoader
{
    /// <summary>
    /// Attempts to load the <c>Microsoft.CodeAnalysis.CSharp.Features</c> assembly.
    /// </summary>
    /// <returns>
    /// The loaded <see cref="Assembly"/>, or <see langword="null"/> if the load failed for any
    /// reason other than cancellation.
    /// </returns>
    internal static Assembly? TryLoad()
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
}
