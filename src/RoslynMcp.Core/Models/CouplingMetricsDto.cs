namespace RoslynMcp.Core.Models;

/// <summary>
/// Coupling metrics for a single type, computed per Robert C. Martin's stability formula.
/// </summary>
/// <param name="TypeName">Short name of the analyzed type.</param>
/// <param name="FullyQualifiedName">Fully qualified name for disambiguation.</param>
/// <param name="FilePath">Absolute path to the declaring file, or <c>null</c> when not in source.</param>
/// <param name="Line">1-based line number of the declaration, or <c>null</c> when not in source.</param>
/// <param name="ProjectName">The containing project's name.</param>
/// <param name="AfferentCoupling">
/// Ca — the number of distinct types OUTSIDE this type that reference it (incoming dependencies).
/// High Ca means many consumers rely on this type; changes here have broad blast radius.
/// </param>
/// <param name="EfferentCoupling">
/// Ce — the number of distinct types OUTSIDE this type that THIS type references (outgoing dependencies).
/// High Ce means this type knows about many other types; it is sensitive to upstream changes.
/// </param>
/// <param name="Instability">
/// I = Ce / (Ca + Ce). 0.0 = maximally stable (all incoming, no outgoing — a sink/leaf).
/// 1.0 = maximally unstable (all outgoing, no incoming — a root/entrypoint).
/// Returns 0.0 when both Ca and Ce are zero (no coupling at all).
/// </param>
/// <param name="Classification">
/// Human-readable bucket derived from <paramref name="Instability"/>:
/// "stable" (I &lt; 0.3), "unstable" (I &gt; 0.7), "balanced" otherwise.
/// Types with Ca + Ce == 0 are reported as "isolated".
/// </param>
public sealed record CouplingMetricsDto(
    string TypeName,
    string FullyQualifiedName,
    string? FilePath,
    int? Line,
    string ProjectName,
    int AfferentCoupling,
    int EfferentCoupling,
    double Instability,
    string Classification)
{
    /// <summary>The kind of type: Class, Struct, Interface, Enum, etc.</summary>
    public string TypeKind { get; init; } = "Class";
}
