namespace RoslynMcp.Core.Models;

/// <summary>
/// A closest-match suggestion for a symbol locator that failed to resolve.
/// </summary>
public sealed record SymbolClosestMatchDto(
    string MetadataName,
    string Kind,
    string? LocationHint);
