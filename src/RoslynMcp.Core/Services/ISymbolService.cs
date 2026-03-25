using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides symbol navigation and analysis operations over a loaded Roslyn workspace.
/// </summary>
public interface ISymbolService
{
    /// <summary>
    /// Searches for symbols matching a name query, optionally filtered by project, kind, or namespace.
    /// </summary>
    Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct);

    /// <summary>
    /// Returns metadata and source location for the symbol identified by <paramref name="locator"/>.
    /// Returns <see langword="null"/> if no symbol is found.
    /// </summary>
    Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns the declaration locations for the symbol identified by <paramref name="locator"/>.
    /// </summary>
    Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds all source locations that reference the symbol identified by <paramref name="locator"/>.
    /// </summary>
    Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds all implementations of the interface member or abstract member identified by <paramref name="locator"/>.
    /// </summary>
    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns the declared symbols in the given source document.
    /// </summary>
    Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct);

    /// <summary>
    /// Returns the type inheritance and interface relationships for the symbol identified by <paramref name="locator"/>.
    /// Returns <see langword="null"/> if no type symbol is found.
    /// </summary>
    Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns the direct callers and callees of the method identified by <paramref name="locator"/>.
    /// Returns <see langword="null"/> if no callable symbol is found.
    /// </summary>
    Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Estimates the impact of changing the symbol identified by <paramref name="locator"/> by collecting
    /// direct references, affected declarations, and affected projects.
    /// Returns <see langword="null"/> if no symbol is found.
    /// </summary>
    Task<ImpactAnalysisDto?> AnalyzeImpactAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds all overrides of the virtual or abstract member identified by <paramref name="locator"/>.
    /// </summary>
    Task<IReadOnlyList<LocationDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds the base members implemented or overridden by the symbol identified by <paramref name="locator"/>.
    /// </summary>
    Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns a summary of base and override relationships for the member identified by <paramref name="locator"/>.
    /// Returns <see langword="null"/> if no member symbol is found.
    /// </summary>
    Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns signature and documentation for the symbol identified by <paramref name="locator"/>.
    /// Returns <see langword="null"/> if no invocable symbol is found.
    /// </summary>
    Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns a combined view of definitions, references, base members, and implementations for the given symbol.
    /// Returns <see langword="null"/> if no symbol is found.
    /// </summary>
    Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds all locations where the property identified by <paramref name="locator"/> is written.
    /// </summary>
    Task<IReadOnlyList<PropertyWriteDto>> FindPropertyWritesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds all usage sites of the type identified by <paramref name="locator"/> and classifies each usage.
    /// </summary>
    Task<IReadOnlyList<TypeUsageDto>> FindTypeUsagesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Resolves references for multiple symbols in a single request.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="symbols">The symbols to look up.</param>
    /// <param name="includeDefinition">When <see langword="true"/>, includes the definition location in each result.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct);

    /// <summary>
    /// Returns state mutation analysis for the type identified by <paramref name="locator"/>.
    /// Returns <see langword="null"/> if no type symbol is found.
    /// </summary>
    Task<TypeMutationDto?> FindTypeMutationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Returns the innermost symbol that encloses the given source position.
    /// Returns <see langword="null"/> if no enclosing symbol is found.
    /// </summary>
    Task<SymbolDto?> GetEnclosingSymbolAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct);

    /// <summary>
    /// Navigates from the symbol usage at the given location to the definition of its type.
    /// </summary>
    Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
}
