namespace RoslynMcp.Core.Models;

/// <summary>
/// Identifies a symbol in a bulk reference request by handle, metadata name, or source location.
/// </summary>
public sealed record BulkSymbolLocator(
    string? SymbolHandle,
    string? MetadataName,
    string? FilePath,
    int? Line,
    int? Column);

/// <summary>
/// Represents the reference results for a single symbol lookup in a bulk reference request.
/// </summary>
public sealed record BulkReferenceResultDto(
    string Key,
    string? ResolvedSymbol,
    int ReferenceCount,
    IReadOnlyList<LocationDto> References,
    string? Error);
