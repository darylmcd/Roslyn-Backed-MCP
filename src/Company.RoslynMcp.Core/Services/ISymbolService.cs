using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface ISymbolService
{
    Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct);

    Task<SymbolDto?> GetSymbolInfoAsync(string filePath, int line, int column, CancellationToken ct);

    Task<SymbolDto?> GetSymbolInfoByNameAsync(string fullyQualifiedName, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string filePath, int line, int column, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string filePath, int line, int column, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string filePath, int line, int column, CancellationToken ct);

    Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string filePath, CancellationToken ct);

    Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string filePath, int line, int column, CancellationToken ct);

    Task<CallerCalleeDto?> GetCallersCalleesAsync(string filePath, int line, int column, CancellationToken ct);

    Task<ImpactAnalysisDto?> AnalyzeImpactAsync(string filePath, int line, int column, CancellationToken ct);
}
