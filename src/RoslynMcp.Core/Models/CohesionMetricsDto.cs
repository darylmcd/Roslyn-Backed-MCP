namespace RoslynMcp.Core.Models;

/// <summary>
/// Cohesion metrics for a single type, including LCOM4 score and method clusters.
/// </summary>
public sealed record CohesionMetricsDto(
    string TypeName,
    string FullyQualifiedName,
    string? FilePath,
    int? Line,
    int MethodCount,
    int FieldCount,
    int Lcom4Score,
    IReadOnlyList<MethodClusterDto> Clusters)
{
    /// <summary>The kind of type: Class, Struct, Interface, Enum, etc.</summary>
    public string TypeKind { get; init; } = "Class";
}

/// <summary>
/// A cluster of methods that share access to the same fields/properties.
/// LCOM4 = number of clusters; 1 = perfectly cohesive.
/// </summary>
public sealed record MethodClusterDto(
    IReadOnlyList<string> Methods,
    /// <summary>Field and property names shared across methods in this cluster (BUG-N9: excludes private method names).</summary>
    IReadOnlyList<string> SharedFields);

/// <summary>
/// A private member used by multiple public methods.
/// </summary>
public sealed record SharedMemberDto(
    string MemberName,
    string Kind,
    string? FilePath,
    int? Line,
    IReadOnlyList<string> CallingMethods);
