namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of analyzing control flow through a code region.
/// </summary>
public sealed record ControlFlowAnalysisDto(
    bool Succeeded,
    bool StartPointIsReachable,
    bool EndPointIsReachable,
    IReadOnlyList<string> EntryPoints,
    IReadOnlyList<string> ExitPoints,
    IReadOnlyList<ReturnStatementDto> ReturnStatements);

/// <summary>
/// Describes a return statement found during control flow analysis.
/// </summary>
public sealed record ReturnStatementDto(
    int Line,
    int Column,
    string? ExpressionText);
