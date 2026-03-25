namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a request to scaffold a new type.
/// </summary>
public sealed record ScaffoldTypeDto(
    string ProjectName,
    string TypeName,
    string TypeKind,
    string? Namespace = null,
    string? BaseType = null,
    IReadOnlyList<string>? Interfaces = null);

/// <summary>
/// Represents a request to scaffold tests for a target type or method.
/// </summary>
public sealed record ScaffoldTestDto(
    string TestProjectName,
    string TargetTypeName,
    string? TargetMethodName = null,
    string TestFramework = "mstest");

/// <summary>
/// Represents a request to remove dead code symbols.
/// </summary>
public sealed record DeadCodeRemovalDto(
    IReadOnlyList<string> SymbolHandles,
    bool RemoveEmptyFiles = false);