namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of applying a previewed workspace mutation.
/// </summary>
public sealed record ApplyResultDto(
    bool Success,
    IReadOnlyList<string> AppliedFiles,
    string? Error);
