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
    IReadOnlyList<MethodClusterDto> Clusters);

/// <summary>
/// A cluster of methods that share access to the same fields/properties.
/// LCOM4 = number of clusters; 1 = perfectly cohesive.
/// </summary>
public sealed record MethodClusterDto(
    IReadOnlyList<string> Methods,
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
