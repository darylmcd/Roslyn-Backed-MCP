using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Navigates to symbol definitions and type definitions, and resolves the enclosing symbol
/// at a given source position.
/// </summary>
public interface ISymbolNavigationService
{
    Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<SymbolDto?> GetEnclosingSymbolAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct);

    /// <summary>
    /// position-probe-for-test-fixture-authoring: returns the raw lexical token + containing
    /// symbol at the given 1-based source position without applying the lenient
    /// adjacent-identifier fallback used by <see cref="GoToDefinitionAsync"/> or symbol resolution.
    /// Intended for test-fixture authoring — a caret on whitespace returns a
    /// <c>Whitespace</c> token classification instead of silently resolving to the next
    /// identifier, so callers can nail down exact caret anchors before pinning them into a
    /// <see cref="SymbolLocator.BySource"/> call.
    /// </summary>
    /// <returns>The probe result, or <see langword="null"/> when the file is not part of the loaded workspace.</returns>
    Task<PositionProbeDto?> ProbePositionAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct);
}
