namespace RoslynMcp.Core.Models;

/// <summary>
/// The response payload for <c>trace_exception_flow</c>. Carries the caller's resolution input,
/// every matching <c>catch</c> clause, and every throw site whose thrown type is assignable to
/// the traced type (catch and throw sites share the same <c>maxResults</c> cap).
/// </summary>
/// <param name="ExceptionTypeMetadataName">The metadata name the caller supplied.</param>
/// <param name="ResolvedTypeDisplayName">The resolved type's display string, or <see langword="null"/> when the metadata name did not resolve to a type in any loaded compilation.</param>
/// <param name="Count">The number of catch sites in <paramref name="CatchSites"/>.</param>
/// <param name="Truncated"><see langword="true"/> when either the catch-site or throw-site list was clipped by <c>maxResults</c>.</param>
/// <param name="CatchSites">The matching catch clauses discovered across every compilation's syntax trees, ranked type-specific (exact-match) first so a broad <c>catch (Exception)</c> never displaces a precise handler when the result is truncated.</param>
/// <param name="ThrowSites">The throw sites whose thrown type is assignable to the traced type, discovered in the same syntax-tree walk as the catch sites.</param>
/// <param name="ThrowSiteCount">The number of throw sites in <paramref name="ThrowSites"/>.</param>
/// <param name="CountOmitted">How many catch + throw sites were discovered beyond the cap and dropped from the response; <c>0</c> when nothing was truncated.</param>
/// <param name="IsComplete"><see langword="false"/> when one or more documents could not be scanned.</param>
/// <param name="FailedDocumentCount">Number of documents omitted after unexpected scan failures.</param>
public sealed record ExceptionFlowResult(
    string ExceptionTypeMetadataName,
    string? ResolvedTypeDisplayName,
    int Count,
    bool Truncated,
    IReadOnlyList<ExceptionCatchSiteDto> CatchSites,
    IReadOnlyList<ExceptionThrowSiteDto> ThrowSites,
    int ThrowSiteCount,
    int CountOmitted,
    bool IsComplete = true,
    int FailedDocumentCount = 0);

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

/// <summary>
/// A single <c>throw new T(...)</c> site whose thrown exception type is assignable to (a subtype
/// of, or equal to) the traced exception type. Complements <see cref="ExceptionCatchSiteDto"/>:
/// catch sites show where the type is handled, throw sites show where it originates.
/// </summary>
/// <param name="FilePath">Absolute path to the source file containing the throw.</param>
/// <param name="Line">1-based line number of the <c>throw</c> keyword.</param>
/// <param name="ContainingMethod">Fully qualified display name of the method, property accessor, or constructor that contains the throw; <see langword="null"/> if it could not be resolved.</param>
/// <param name="IsUnhandledAtBoundary"><see langword="true"/> when the throw is NOT lexically enclosed in a <c>catch</c> clause (i.e. it propagates out of the local handling site). This is a purely syntactic classification — no escape/data-flow analysis is performed, so a throw inside a lambda or local function nested in a catch clause is reported as enclosed even though it may execute outside the catch's dynamic extent.</param>
/// <param name="ExpressionExcerpt">First ~200 characters of the throw expression source (whitespace-normalized) so agents can see the thrown construction without opening the file.</param>
public sealed record ExceptionThrowSiteDto(
    string FilePath,
    int Line,
    string? ContainingMethod,
    bool IsUnhandledAtBoundary,
    string ExpressionExcerpt);
