using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Traces every <c>catch</c> clause across a workspace whose declared exception type is
/// assignable from (or equal to) a caller-supplied exception type. Surfaces body excerpts
/// plus rethrow-as-different-type annotations so exception-classification refactors can
/// enumerate handling sites quickly.
/// </summary>
public interface IExceptionFlowService
{
    /// <summary>
    /// Walks each compilation's syntax trees, finds every <c>CatchClauseSyntax</c>, resolves
    /// the declared exception type via the semantic model, and returns each catch site whose
    /// declared type is assignable from the input (or equal to it). Untyped <c>catch { }</c>
    /// clauses are treated as catching <see cref="System.Exception"/>.
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
