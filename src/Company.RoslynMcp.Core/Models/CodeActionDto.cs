namespace Company.RoslynMcp.Core.Models;

public sealed record CodeActionDto(
    int Index,
    string Title,
    string Kind,
    string? EquivalenceKey);
