namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a namespace dependency graph for one or more projects.
/// </summary>
/// <param name="Nodes">Namespace nodes (declared or referenced) in the analyzed scope.</param>
/// <param name="Edges">Namespace-to-namespace dependency edges with reference counts.</param>
/// <param name="CircularDependencies">Cycles detected via DFS over the edge set.</param>
/// <param name="AnalyzedProjectCount">
/// Number of projects whose compilations were successfully walked. Lets callers distinguish
/// "no cycles found across N projects" from "analysis did not run" when <paramref name="CircularDependencies"/>
/// is empty — a gap that previously surfaced on large multi-project solutions (gh #615).
/// </param>
/// <param name="TotalNamespacesScanned">
/// Total count of distinct namespace declarations encountered during the walk. Equals
/// <paramref name="Nodes"/>.Count when no filtering has been applied at the tool-wrapper layer
/// (the <c>circularOnly</c> filter can reduce <paramref name="Nodes"/> below this value).
/// </param>
public sealed record NamespaceDependencyGraphDto(
    IReadOnlyList<NamespaceNodeDto> Nodes,
    IReadOnlyList<NamespaceEdgeDto> Edges,
    IReadOnlyList<CircularDependencyDto> CircularDependencies,
    int AnalyzedProjectCount,
    int TotalNamespacesScanned);

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
