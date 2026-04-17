namespace RoslynMcp.Core.Models;

/// <summary>
/// The response payload for <c>trace_exception_flow</c>. Carries the caller's resolution input
/// plus every matching <c>catch</c> clause.
/// </summary>
/// <param name="ExceptionTypeMetadataName">The metadata name the caller supplied.</param>
/// <param name="ResolvedTypeDisplayName">The resolved type's display string, or <see langword="null"/> when the metadata name did not resolve to a type in any loaded compilation.</param>
/// <param name="Count">The number of catch sites in <paramref name="CatchSites"/>.</param>
/// <param name="Truncated"><see langword="true"/> when results were clipped by <c>maxResults</c>.</param>
/// <param name="CatchSites">The matching catch clauses discovered across every compilation's syntax trees.</param>
public sealed record ExceptionFlowResult(
    string ExceptionTypeMetadataName,
    string? ResolvedTypeDisplayName,
    int Count,
    bool Truncated,
    IReadOnlyList<ExceptionCatchSiteDto> CatchSites);

/// <summary>
/// A single <c>catch</c> clause whose declared exception type is assignable from (or equal to)
/// the traced exception type.
/// </summary>
/// <param name="FilePath">Absolute path to the source file containing the catch.</param>
/// <param name="Line">1-based line number of the catch keyword.</param>
/// <param name="ContainingMethod">Fully qualified display name of the method, property accessor, or constructor that contains the catch; <see langword="null"/> if it could not be resolved.</param>
/// <param name="DeclaredExceptionTypeMetadataName">Fully qualified metadata name of the declared exception type on the catch (or <c>System.Exception</c> for untyped <c>catch { }</c>).</param>
/// <param name="CatchesBaseException"><see langword="true"/> when the declared type is a base of the traced type (wider catch) rather than an exact match. Wider catches still match because any thrown instance of the traced type would be handled here.</param>
/// <param name="HasFilter"><see langword="true"/> when the catch carries a <c>when</c> filter clause; the filter source is included in <see cref="BodyExcerpt"/>.</param>
/// <param name="BodyExcerpt">First ~200 characters of the catch body (with the <c>when</c> filter prepended when present) to help agents see what the handler does without opening the file.</param>
/// <param name="RethrowAsTypeMetadataName">When the catch body wraps the caught exception in <c>throw new X(...)</c> and <c>X</c> is different from the declared type, the metadata name of <c>X</c>; <see langword="null"/> otherwise (including for bare <c>throw;</c>).</param>
public sealed record ExceptionCatchSiteDto(
    string FilePath,
    int Line,
    string? ContainingMethod,
    string DeclaredExceptionTypeMetadataName,
    bool CatchesBaseException,
    bool HasFilter,
    string BodyExcerpt,
    string? RethrowAsTypeMetadataName);
