namespace Company.RoslynMcp.Core.Models;

public sealed record NamespaceDependencyGraphDto(
    IReadOnlyList<NamespaceNodeDto> Nodes,
    IReadOnlyList<NamespaceEdgeDto> Edges,
    IReadOnlyList<CircularDependencyDto> CircularDependencies);

public sealed record NamespaceNodeDto(
    string Namespace,
    int TypeCount,
    string? Project);

public sealed record NamespaceEdgeDto(
    string FromNamespace,
    string ToNamespace,
    int ReferenceCount);

public sealed record CircularDependencyDto(
    IReadOnlyList<string> Cycle);
