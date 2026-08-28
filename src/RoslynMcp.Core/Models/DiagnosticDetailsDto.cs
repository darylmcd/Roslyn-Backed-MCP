namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents detailed information for a diagnostic, including supported code fixes.
/// </summary>
public sealed record DiagnosticDetailsDto(
    DiagnosticDto Diagnostic,
    string? Description,
    string? HelpLinkUri,
    IReadOnlyList<CodeFixOptionDto> SupportedFixes,
    string? GuidanceMessage = null,
    bool FixEnumerationComplete = true,
    int FailedProviderCount = 0);

/// <summary>
/// Represents a code fix option available for a diagnostic.
/// </summary>
public sealed record CodeFixOptionDto(
    string FixId,
    string Title,
    string Description);
