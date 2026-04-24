namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a source-declared field whose observed source usage is incomplete:
/// never read, never written, or never either.
/// See <see cref="IUnusedCodeAnalyzer.FindDeadFieldsAsync"/> for the heuristic that
/// produces these hits.
/// </summary>
/// <param name="SymbolName">The field name (e.g. <c>_count</c>).</param>
/// <param name="ContainingType">The declaring type that owns the field.</param>
/// <param name="FilePath">Absolute path to the source file declaring the field.</param>
/// <param name="Line">1-based declaration line.</param>
/// <param name="Column">1-based declaration column.</param>
/// <param name="ProjectName">Roslyn project containing the field.</param>
/// <param name="UsageKind">One of <c>never-read</c>, <c>never-written</c>, or <c>never-either</c>.</param>
/// <param name="ReadReferenceCount">Number of source references classified as reads.</param>
/// <param name="WriteReferenceCount">Number of source references classified as writes, plus a declaration initializer when present.</param>
/// <param name="SymbolHandle">Stable symbol handle for follow-up tools.</param>
/// <param name="Confidence">Heuristic confidence: high (non-public), medium (public/protected surface).</param>
/// <param name="RemovalBlockedBy">
/// Non-null when the classifier found residual source references that would make
/// <c>remove_dead_code_preview</c> refuse ("still has references"). Each entry is a
/// marker string of the form <c>Kind@AbsolutePath:Line:Column</c>, where <c>Kind</c>
/// is one of <c>ConstructorWrite</c>, <c>Read</c>, or <c>Write</c>. <c>null</c> when
/// no residual references exist beyond a declaration initializer.
/// </param>
/// <param name="SafelyRemovable">
/// True when <c>remove_dead_code_preview</c> is expected to succeed — the field has
/// no residual source references (only the declaration initializer is tolerated).
/// False when any source reference survives, including constructor writes; in that
/// case <see cref="RemovalBlockedBy"/> enumerates the blocking sites so callers
/// don't chain a removal workflow that always refuses.
/// </param>
public sealed record DeadFieldDto(
    string SymbolName,
    string? ContainingType,
    string FilePath,
    int Line,
    int Column,
    string ProjectName,
    string UsageKind,
    int ReadReferenceCount,
    int WriteReferenceCount,
    string? SymbolHandle,
    string Confidence,
    IReadOnlyList<string>? RemovalBlockedBy,
    bool SafelyRemovable);
