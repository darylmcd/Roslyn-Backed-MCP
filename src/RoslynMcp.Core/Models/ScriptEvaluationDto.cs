namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of evaluating a C# script expression or code block.
/// </summary>
public sealed record ScriptEvaluationDto(
    bool Success,
    string? ResultType,
    string? ResultValue,
    string? Error,
    IReadOnlyList<DiagnosticDto>? CompilationErrors,
    long ElapsedMs,
    int? AppliedScriptTimeoutSeconds = null);
