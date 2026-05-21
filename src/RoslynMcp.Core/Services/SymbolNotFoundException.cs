using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Not-found error for symbol locators that can include closest-match suggestions.
/// </summary>
public sealed class SymbolNotFoundException : KeyNotFoundException
{
    public SymbolNotFoundException(string message, IReadOnlyList<SymbolClosestMatchDto> closestMatches)
        : base(message)
    {
        ClosestMatches = closestMatches;
    }

    public IReadOnlyList<SymbolClosestMatchDto> ClosestMatches { get; }
}
