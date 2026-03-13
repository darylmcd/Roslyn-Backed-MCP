namespace Company.RoslynMcp.Core.Models;

public sealed record SignatureHelpDto(
    string DisplaySignature,
    string? ReturnType,
    IReadOnlyList<string> Parameters,
    string? Documentation);
