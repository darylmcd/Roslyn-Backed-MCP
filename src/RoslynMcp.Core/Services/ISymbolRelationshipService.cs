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

    /// <summary>
    /// Combined relationships view. When <paramref name="preferDeclaringMember"/> is true (default),
    /// a caret that lands on a type token inside a member declaration is auto-promoted to the
    /// enclosing member symbol so callers don't accidentally analyze the type instead of the
    /// member they were pointing at (FLAG-006).
    /// </summary>
    Task<SymbolRelationshipsDto?> GetSymbolRelationshipsAsync(string workspaceId, SymbolLocator locator, bool preferDeclaringMember, CancellationToken ct);

    /// <summary>
    /// Signature help for the symbol at the locator. When <paramref name="preferDeclaringMember"/>
    /// is true (default), a caret that lands on a type token inside a method or property declaration
    /// is auto-promoted to the enclosing member symbol so the returned signature describes the
    /// member, not its return type (FLAG-006).
    /// </summary>
    Task<SignatureHelpDto?> GetSignatureHelpAsync(string workspaceId, SymbolLocator locator, bool preferDeclaringMember, CancellationToken ct);

    Task<CallerCalleeDto?> GetCallersCalleesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
}
