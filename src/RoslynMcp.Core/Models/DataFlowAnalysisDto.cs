using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of analyzing data flow through a code region.
/// </summary>
/// <param name="Warning">
/// Set when the requested line range could not be analyzed as-is and was narrowed to a
/// single-block statement group (e.g. the range spans two methods). Null otherwise and
/// omitted from the wire shape.
/// </param>
/// <param name="EffectiveStartLine">1-based first line actually analyzed when the range was narrowed; null otherwise.</param>
/// <param name="EffectiveEndLine">1-based last line actually analyzed when the range was narrowed; null otherwise.</param>
public sealed record DataFlowAnalysisDto(
    bool Succeeded,
    IReadOnlyList<string> VariablesDeclared,
    IReadOnlyList<string> DataFlowsIn,
    IReadOnlyList<string> DataFlowsOut,
    IReadOnlyList<string> AlwaysAssigned,
    IReadOnlyList<string> ReadInside,
    IReadOnlyList<string> WrittenInside,
    IReadOnlyList<string> ReadOutside,
    IReadOnlyList<string> WrittenOutside,
    IReadOnlyList<string> Captured,
    IReadOnlyList<string> CapturedInside,
    IReadOnlyList<string> UnsafeAddressTaken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Warning = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EffectiveStartLine = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EffectiveEndLine = null);
