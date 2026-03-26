using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides combined summaries of type hierarchy, member hierarchy, symbol relationships,
/// signature help, and caller/callee analysis for a symbol.
/// </summary>
public interface ISymbolRelationshipService
{
    Task<TypeHierarchyDto?> GetTypeHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<MemberHierarchyDto?> GetMemberHierarchyAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
}
