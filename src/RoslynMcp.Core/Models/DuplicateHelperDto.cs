namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a private or internal helper method whose body shape appears to
/// duplicate a reachable canonical symbol from the BCL or a referenced NuGet
/// package. See <see cref="IUnusedCodeAnalyzer.FindDuplicateHelpersAsync"/> for
/// the heuristic that produces these hits.
/// </summary>
/// <param name="SymbolName">Name of the local helper method (e.g. <c>IsNullOrWhiteSpace</c>).</param>
/// <param name="ContainingType">The declaring static helper type (e.g. <c>StringHelper</c>).</param>
/// <param name="FilePath">Absolute path to the source file declaring the helper.</param>
/// <param name="Line">1-based declaration line.</param>
/// <param name="Column">1-based declaration column.</param>
/// <param name="ProjectName">Roslyn project containing the helper.</param>
/// <param name="CanonicalTarget">Fully-qualified display string of the canonical symbol the body delegates to (e.g. <c>string.IsNullOrWhiteSpace(string)</c>).</param>
/// <param name="CanonicalTargetAssembly">Name of the assembly (BCL/NuGet) the canonical symbol lives in — the signal that the helper is re-inventing a library primitive.</param>
/// <param name="Confidence">Heuristic confidence: <c>high</c> (single-statement single-argument delegation), <c>medium</c> (two-statement null-guarded delegation).</param>
public sealed record DuplicateHelperDto(
    string SymbolName,
    string? ContainingType,
    string FilePath,
    int Line,
    int Column,
    string ProjectName,
    string CanonicalTarget,
    string CanonicalTargetAssembly,
    string Confidence);
