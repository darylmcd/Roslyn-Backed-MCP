namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents signature help information for an invocation site.
/// </summary>
public sealed record SignatureHelpDto(
    string DisplaySignature,
    string? ReturnType,
    IReadOnlyList<string> Parameters,
    string? Documentation);
