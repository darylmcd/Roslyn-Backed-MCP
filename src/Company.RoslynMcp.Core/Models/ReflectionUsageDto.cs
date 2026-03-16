namespace Company.RoslynMcp.Core.Models;

public sealed record ReflectionUsageDto(
    string UsageKind,
    string CalledMethod,
    string FilePath,
    int Line,
    int Column,
    string? ContainingMethod,
    string? TypeArgument);
