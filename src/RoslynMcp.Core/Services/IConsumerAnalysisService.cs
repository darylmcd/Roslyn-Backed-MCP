using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Analyzes which types depend on a given type or interface via constructor injection,
/// fields, method parameters, base types, or local variables.
/// </summary>
public interface IConsumerAnalysisService
{
    /// <summary>
    /// Finds all types that consume (depend on) the type identified by <paramref name="locator"/>,
    /// classified by dependency kind (Constructor, Field, Parameter, BaseType, LocalVariable).
    /// </summary>
    /// <param name="projectFilter">
    /// Optional case-sensitive set of <c>Project.Name</c> values used to scope the reference
    /// walk. When non-null and non-empty, references whose document belongs to a non-matching
    /// project are dropped before consumer classification (matches <c>semantic_grep</c>'s
    /// <c>projectFilter</c> semantics). When null/empty, behavior is byte-identical to the
    /// unfiltered call.
    /// </param>
    Task<ConsumerAnalysisDto?> FindConsumersAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct, IReadOnlyCollection<string>? projectFilter = null);
}
