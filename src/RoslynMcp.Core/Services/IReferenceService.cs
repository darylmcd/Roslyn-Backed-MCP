using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds all references, implementations, overrides, and base members for a symbol across
/// a solution, with classified usage locations and bulk lookup support.
/// </summary>
public interface IReferenceService
{
    /// <summary>
    /// Finds every reference to the symbol resolved at <paramref name="locator"/>.
    /// </summary>
    /// <param name="summary">
    /// When true (find-references-preview-text-inflates-response): suppresses per-ref
    /// preview text so the response stays small for high-fan-out symbols. Each returned
    /// <see cref="LocationDto"/> has <c>PreviewText = null</c>; file path + line + column
    /// + classification still populated. Default <c>false</c> preserves the v1.18.2 shape.
    /// </param>
    /// <param name="projectFilter">
    /// Optional case-sensitive set of <c>Project.Name</c> values to scope the result to.
    /// When non-null and non-empty, only references whose document belongs to a matching
    /// project are returned (matches <c>semantic_grep</c>'s <c>projectFilter</c> semantics).
    /// When null/empty, behavior is byte-identical to the unfiltered call.
    /// </param>
    Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct, bool summary = false, IReadOnlyCollection<string>? projectFilter = null);
    /// <summary>
    /// Finds every implementation of an interface or abstract member resolved at
    /// <paramref name="locator"/>.
    /// </summary>
    /// <param name="includeGeneratedPartials">
    /// When false (default — find-implementations-source-gen-partial-dedup): the result contains
    /// one location per unique implementation symbol, preferring the user-authored partial
    /// declaration over any source-generator-emitted partial (e.g. <c>Logging.g.cs</c>,
    /// <c>RegexGenerator.g.cs</c>). This prevents the same type from appearing 3+ times in the
    /// result list simply because <c>[LoggerMessage]</c> or <c>[GeneratedRegex]</c> expanded it
    /// into extra partial declarations. When true, every source location of every
    /// implementation symbol is emitted (the v1 behavior).
    /// </param>
    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(
        string workspaceId,
        SymbolLocator locator,
        CancellationToken ct,
        bool includeGeneratedPartials = false);
    /// <summary>
    /// Finds true virtual/abstract overrides for the member resolved at <paramref name="locator"/> —
    /// only members actually marked <c>override</c> of a virtual or abstract declaration. Sibling
    /// interface implementations (e.g., independent <c>IDisposable.Dispose</c> implementations across
    /// a solution) are NOT included; use
    /// <see cref="FindSiblingInterfaceImplementationsAsync"/> for that bucket. Returns
    /// <see cref="SymbolDto"/> rather than <see cref="LocationDto"/> so that members whose definition
    /// lives in metadata (e.g. <c>IEquatable&lt;T&gt;.Equals</c>) are not silently dropped — a
    /// metadata-only result still carries <see cref="SymbolDto.FullyQualifiedName"/> with
    /// <see cref="SymbolDto.FilePath"/>=<c>null</c>. Aligns with <c>member_hierarchy.overrides</c>.
    /// </summary>
    Task<IReadOnlyList<SymbolDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds sibling interface implementations for the member resolved at <paramref name="locator"/> —
    /// every concrete type in the solution whose member fulfills the interface contract
    /// (<c>type.FindImplementationForInterfaceMember(symbol)</c>). When the locator resolves to a
    /// non-interface member (e.g. a virtual method on a class) the result is empty. Aligns with
    /// <c>member_hierarchy.siblingInterfaceImplementations</c>.
    /// </summary>
    Task<IReadOnlyList<SymbolDto>> FindSiblingInterfaceImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// Finds base or implemented members for an override or implementation resolved at
    /// <paramref name="locator"/>. Returns <see cref="SymbolDto"/> rather than <see cref="LocationDto"/> so
    /// that members whose definition lives in metadata (e.g. <c>IEquatable&lt;T&gt;.Equals</c>) are not
    /// silently dropped — a metadata-only result still carries <see cref="SymbolDto.FullyQualifiedName"/>
    /// with <see cref="SymbolDto.FilePath"/>=<c>null</c>. Aligns with <c>member_hierarchy</c>.
    /// </summary>
    Task<IReadOnlyList<SymbolDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct);
}
