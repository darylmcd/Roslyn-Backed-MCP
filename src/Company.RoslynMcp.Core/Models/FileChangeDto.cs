namespace Company.RoslynMcp.Core.Models;

public sealed record FileChangeDto(
    string FilePath,
    string UnifiedDiff);
