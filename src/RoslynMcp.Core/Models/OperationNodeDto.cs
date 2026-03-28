namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a node in the Roslyn IOperation tree — a language-agnostic intermediate
/// representation of code behavior (assignments, invocations, loops, etc.).
/// </summary>
public sealed record OperationNodeDto(
    string Kind,
    string? Type,
    string? ConstantValue,
    string? Syntax,
    int Line,
    int Column,
    IReadOnlyList<OperationNodeDto>? Children);
