using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides access to the Roslyn IOperation tree for behavioral analysis of code.
/// </summary>
public interface IOperationService
{
    /// <summary>
    /// Gets the IOperation tree for the syntax node at the given source position.
    /// </summary>
    Task<OperationNodeDto?> GetOperationsAsync(
        string workspaceId, string filePath, int line, int column, int maxDepth, CancellationToken ct);
}
