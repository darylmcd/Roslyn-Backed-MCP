using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Traces every <c>catch</c> clause AND every <c>throw new T(...)</c> site across a workspace
/// related to a caller-supplied exception type: catch clauses whose declared type is assignable
/// from (or equal to) the input, and throw sites whose thrown type is assignable to it. Surfaces
/// body excerpts, rethrow-as-different-type annotations, and a syntactic "unhandled at boundary"
/// flag so exception-classification refactors can enumerate both handling and origination sites.
/// </summary>
public interface IExceptionFlowService
{
    /// <summary>
    /// Walks each compilation's syntax trees once, finding both every <c>CatchClauseSyntax</c>
    /// and every <c>throw</c> of an object-creation expression, resolving types via the semantic
    /// model. Returns each catch site whose declared type is assignable from the input (or equal
    /// to it) — untyped <c>catch { }</c> clauses are treated as catching <see cref="System.Exception"/>
    /// — and each throw site whose thrown type is assignable to the input. Catch sites are ranked
    /// type-specific (exact-match) first, so a broad <c>catch (Exception)</c> never displaces a
    /// precise handler when the result is clipped by <paramref name="maxResults"/>. The
    /// "unhandled at boundary" classification on throw sites is purely syntactic (lexical
    /// enclosure in a <c>catch</c> clause); no escape or data-flow analysis is performed.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="exceptionTypeMetadataName">Fully qualified metadata name of the exception type to trace (e.g. <c>System.Text.Json.JsonException</c>).</param>
    /// <param name="scopeProjectFilter">Optional: restrict the walk to a specific project name.</param>
    /// <param name="maxResults">Optional: cap the returned catch-site list (default: 200).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExceptionFlowResult> TraceExceptionFlowAsync(
        string workspaceId,
        string exceptionTypeMetadataName,
        string? scopeProjectFilter,
        int? maxResults,
        CancellationToken ct);
}
