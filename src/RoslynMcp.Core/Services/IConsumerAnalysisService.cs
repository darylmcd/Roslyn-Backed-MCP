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
    Task<ConsumerAnalysisDto?> FindConsumersAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct);
}
