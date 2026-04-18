namespace RoslynMcp.Core.Models;

/// <summary>
/// A cluster of methods whose bodies are structurally identical (or very close) after
/// AST normalization — candidates for deduplication or extraction to a shared helper.
/// </summary>
/// <param name="NormalizedHash">
/// Hash of the canonical syntax-kind + identifier-ordinal string for the bucket. Stable
/// across method-name, local-variable-name, and parameter-name differences. Provided as
/// a debugging aid — callers should not rely on a specific hash algorithm.
/// </param>
/// <param name="MemberCount">Number of methods in the group (always &gt;= 2).</param>
/// <param name="Similarity">
/// Structural similarity score in [0.0, 1.0]. Exact structural duplicates score 1.0;
/// near-matches produced by length-scaled comparison of the canonical sequences score
/// lower. Groups below the request's threshold are dropped before this DTO is emitted.
/// </param>
/// <param name="LineCount">Body line span of the first method in the group (for quick triage).</param>
/// <param name="Methods">The member locations that cluster together.</param>
public sealed record DuplicatedMethodGroupDto(
    string NormalizedHash,
    int MemberCount,
    double Similarity,
    int LineCount,
    IReadOnlyList<DuplicatedMethodMemberDto> Methods);

/// <summary>
/// One method-body occurrence inside a <see cref="DuplicatedMethodGroupDto"/>.
/// </summary>
/// <param name="FilePath">Absolute path to the file declaring the method.</param>
/// <param name="StartLine">1-based inclusive start line of the method declaration.</param>
/// <param name="EndLine">1-based inclusive end line of the method declaration.</param>
/// <param name="MethodName">The method's simple name.</param>
/// <param name="ContainingType">Fully qualified containing type (namespace + type name) or <see langword="null"/> when unresolved.</param>
/// <param name="ProjectName">Owning project name.</param>
public sealed record DuplicatedMethodMemberDto(
    string FilePath,
    int StartLine,
    int EndLine,
    string MethodName,
    string? ContainingType,
    string ProjectName);
