namespace Company.RoslynMcp.Core.Models;

public sealed record ApplyResultDto(
    bool Success,
    IReadOnlyList<string> AppliedFiles,
    string? Error);
