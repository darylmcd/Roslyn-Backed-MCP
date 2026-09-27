using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of analyzing control flow through a code region.
/// </summary>
/// <param name="EffectiveStartLine">
/// 1-based first line actually analyzed when the requested range was narrowed to a
/// single-block statement group (see <paramref name="Warning"/>); null otherwise and omitted
/// from the wire shape.
/// </param>
/// <param name="EffectiveEndLine">1-based last line actually analyzed when the range was narrowed; null otherwise.</param>
public sealed record ControlFlowAnalysisDto(
    bool Succeeded,
    bool StartPointIsReachable,
    bool EndPointIsReachable,
    IReadOnlyList<string> EntryPoints,
    IReadOnlyList<string> ExitPoints,
    IReadOnlyList<ReturnStatementDto> ReturnStatements,
    string? Warning = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EffectiveStartLine = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EffectiveEndLine = null);

/// <summary>
/// Describes a return statement found during control flow analysis.
/// </summary>
public sealed record ReturnStatementDto(
    int Line,
    int Column,
    string? ExpressionText);
