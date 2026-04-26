using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Resolves a type by name and returns a file-granularity rollup of every source file that
/// references the type, classified by usage kind.
/// </summary>
/// <remarks>
/// find-type-consumers-file-granularity-rollup: backs the <c>find_type_consumers</c> MCP tool.
/// </remarks>
public interface ITypeConsumersService
{
    /// <summary>
    /// Finds every reference to the named type and groups the results by file path, returning
    /// one rollup entry per file with the deduped set of usage kinds and total site count.
    /// </summary>
    /// <param name="workspaceId">Workspace session identifier.</param>
    /// <param name="typeName">
    /// Type name to resolve — accepts an unqualified short name, a partial qualifier
    /// (<c>Namespace.TypeName</c>), or a fully qualified metadata name. Generic arity is encoded
    /// the standard way (<c>List`1</c>).
    /// </param>
    /// <param name="limit">Maximum number of file rollup entries to return (default 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// File rollup entries sorted by descending site <see cref="TypeConsumerFileRollupDto.Count"/>
    /// then ascending <see cref="TypeConsumerFileRollupDto.FilePath"/> for deterministic ordering.
    /// </returns>
    Task<IReadOnlyList<TypeConsumerFileRollupDto>> FindTypeConsumersAsync(
        string workspaceId,
        string typeName,
        int limit,
        CancellationToken ct);
}
