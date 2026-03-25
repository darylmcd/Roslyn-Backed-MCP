namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a namespace dependency graph for one or more projects.
/// </summary>
public sealed record NamespaceDependencyGraphDto(
    IReadOnlyList<NamespaceNodeDto> Nodes,
    IReadOnlyList<NamespaceEdgeDto> Edges,
    IReadOnlyList<CircularDependencyDto> CircularDependencies);

/// <summary>
/// Represents a namespace node in a dependency graph.
/// </summary>
public sealed record NamespaceNodeDto(
    string Namespace,
    int TypeCount,
    string? Project);

/// <summary>
/// Represents a dependency edge between two namespaces.
/// </summary>
public sealed record NamespaceEdgeDto(
    string FromNamespace,
    string ToNamespace,
    int ReferenceCount);

/// <summary>
/// Represents a detected circular dependency between namespaces.
/// </summary>
public sealed record CircularDependencyDto(
    IReadOnlyList<string> Cycle);
