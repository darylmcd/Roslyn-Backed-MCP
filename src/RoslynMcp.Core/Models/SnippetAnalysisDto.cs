namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of analyzing a C# code snippet in an ephemeral workspace.
/// </summary>
public sealed record SnippetAnalysisDto(
    bool IsValid,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    IReadOnlyList<string>? DeclaredSymbols);
