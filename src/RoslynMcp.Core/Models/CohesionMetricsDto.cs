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

    /// <summary>
    /// Well-known lifecycle pattern detected on the type, or <c>null</c> if none.
    /// Types matching a known lifecycle pattern are expected to have high LCOM4 by design
    /// (their public methods are orthogonal on fields), so the split recommendation is downgraded.
    /// Current values: <c>"action-triad"</c> (type name ending in Action/Handler/Command/Stage
    /// with a Describe + Validate* + Execute* method triad). Reserved for future patterns.
    /// </summary>
    public string? LifecyclePattern { get; init; }

    /// <summary>
    /// Human-readable recommendation for the LCOM4 score. When a lifecycle pattern is detected,
    /// this is softened to explain that a high LCOM4 is expected by design. <c>null</c> when no
    /// special-case guidance applies (callers should fall back to the default "split" suggestion).
    /// </summary>
    public string? Recommendation { get; init; }
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
