namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a reflection-based usage discovered in source code.
/// </summary>
public sealed record ReflectionUsageDto(
    string UsageKind,
    string CalledMethod,
    string FilePath,
    int Line,
    int Column,
    string? ContainingMethod,
    string? TypeArgument);

/// <summary>
/// Completeness-aware reflection scan result. <see cref="Usages"/> remains usable when one or
/// more documents fail, but callers must treat its count as an observed lower bound unless
/// <see cref="IsComplete"/> is <see langword="true"/>.
/// </summary>
public sealed record ReflectionUsageScanResult(
    IReadOnlyList<ReflectionUsageDto> Usages,
    bool IsComplete,
    int FailedDocumentCount);
