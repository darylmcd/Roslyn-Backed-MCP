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
