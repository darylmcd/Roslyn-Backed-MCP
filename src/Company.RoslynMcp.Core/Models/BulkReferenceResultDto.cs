namespace Company.RoslynMcp.Core.Models;

public sealed record BulkSymbolLocator(
    string? SymbolHandle,
    string? MetadataName,
    string? FilePath,
    int? Line,
    int? Column);

public sealed record BulkReferenceResultDto(
    string Key,
    string? ResolvedSymbol,
    int ReferenceCount,
    IReadOnlyList<LocationDto> References,
    string? Error);
