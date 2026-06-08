using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Optional Roslyn-side extension for callers that already resolved a symbol for wrapper-level
/// guards and should not pay for a second locator resolution.
/// </summary>
public interface IPreResolvedReferenceService
{
    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(
        string workspaceId,
        ISymbol symbol,
        CancellationToken ct,
        bool includeGeneratedPartials = false);
}
