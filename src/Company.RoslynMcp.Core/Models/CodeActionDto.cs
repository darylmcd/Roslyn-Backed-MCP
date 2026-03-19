namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a code action offered for a diagnostic or refactoring request.
/// </summary>
public sealed record CodeActionDto(
    int Index,
    string Title,
    string Kind,
    string? EquivalenceKey);
