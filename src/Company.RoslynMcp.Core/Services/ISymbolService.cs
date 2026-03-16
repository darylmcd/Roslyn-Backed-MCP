using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface ISymbolService
{
    Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct);

    Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct);

    Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<ImpactAnalysisDto?> AnalyzeImpactAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<PropertyWriteDto>> FindPropertyWritesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<TypeUsageDto>> FindTypeUsagesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct);

    Task<TypeMutationDto?> FindTypeMutationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
}
