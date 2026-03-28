namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of analyzing data flow through a code region.
/// </summary>
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
    IReadOnlyList<string> UnsafeAddressTaken);
