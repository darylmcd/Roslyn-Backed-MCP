namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents the unified diff for a single changed file.
/// </summary>
public sealed record FileChangeDto(
    string FilePath,
    string UnifiedDiff);
