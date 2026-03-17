namespace Company.RoslynMcp.Core.Models;

public sealed record ScaffoldTypeDto(
    string ProjectName,
    string TypeName,
    string TypeKind,
    string? Namespace = null,
    string? BaseType = null,
    IReadOnlyList<string>? Interfaces = null);

public sealed record ScaffoldTestDto(
    string TestProjectName,
    string TargetTypeName,
    string? TargetMethodName = null,
    string TestFramework = "mstest");

public sealed record DeadCodeRemovalDto(
    IReadOnlyList<string> SymbolHandles,
    bool RemoveEmptyFiles = false);