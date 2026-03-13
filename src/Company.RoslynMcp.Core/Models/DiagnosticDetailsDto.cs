namespace Company.RoslynMcp.Core.Models;

public sealed record DiagnosticDetailsDto(
    DiagnosticDto Diagnostic,
    string? Description,
    string? HelpLinkUri,
    IReadOnlyList<CodeFixOptionDto> SupportedFixes);

public sealed record CodeFixOptionDto(
    string FixId,
    string Title,
    string Description);
