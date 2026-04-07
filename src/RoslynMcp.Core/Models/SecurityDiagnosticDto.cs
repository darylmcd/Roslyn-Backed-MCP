namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a single security-relevant diagnostic finding enriched with OWASP categorization.
/// </summary>
/// <param name="DiagnosticId">The diagnostic identifier (e.g., <c>CA3001</c>).</param>
/// <param name="Message">The compiler/analyzer diagnostic message.</param>
/// <param name="SecurityCategory">Security category (e.g., "Injection", "Cryptography").</param>
/// <param name="OwaspCategory">OWASP Top 10 category (e.g., "A03:2021 Injection").</param>
/// <param name="SecuritySeverity">Security severity: Critical, High, Medium, or Low.</param>
/// <param name="FilePath">Absolute path to the source file, or <see langword="null"/> if not file-specific.</param>
/// <param name="StartLine">1-based start line, or <see langword="null"/> if not available.</param>
/// <param name="StartColumn">1-based start column, or <see langword="null"/> if not available.</param>
/// <param name="FixHint">Short actionable fix description, or <see langword="null"/>.</param>
/// <param name="HelpLinkUri">Link to documentation for this diagnostic, or <see langword="null"/>.</param>
public sealed record SecurityDiagnosticDto(
    string DiagnosticId,
    string Message,
    string SecurityCategory,
    string OwaspCategory,
    string SecuritySeverity,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    string? FixHint,
    string? HelpLinkUri);

/// <summary>
/// Result of a security diagnostic scan, containing findings and analyzer status.
/// </summary>
/// <param name="Findings">The security-relevant diagnostic findings.</param>
/// <param name="AnalyzerStatus">Status of security analyzer packages in the workspace.</param>
/// <param name="TotalFindings">Total number of security findings.</param>
/// <param name="CriticalCount">Count of Critical severity findings.</param>
/// <param name="HighCount">Count of High severity findings.</param>
/// <param name="MediumCount">Count of Medium severity findings.</param>
/// <param name="LowCount">Count of Low severity findings.</param>
public sealed record SecurityDiagnosticsResultDto(
    IReadOnlyList<SecurityDiagnosticDto> Findings,
    SecurityAnalyzerStatusDto AnalyzerStatus,
    int TotalFindings,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount);

/// <summary>
/// Status of security analyzer packages detected in the workspace.
/// </summary>
/// <param name="NetAnalyzersPresent">Whether .NET SDK analyzers are present (always true for SDK-style .NET 5+ projects).</param>
/// <param name="SecurityCodeScanPresent">Whether the SecurityCodeScan analyzer is loaded or referenced as a package.</param>
/// <param name="MissingRecommendedPackages">Recommended security analyzer packages that are not currently referenced.</param>
/// <param name="PumaSecurityRulesPresent">Whether Puma.Security.Rules (or related) is detected via analyzers or PackageReference.</param>
/// <param name="SecurityRelatedNuGetPackages">PackageReference identities that appear security-related (subset of evaluated MSBuild items).</param>
public sealed record SecurityAnalyzerStatusDto(
    bool NetAnalyzersPresent,
    bool SecurityCodeScanPresent,
    IReadOnlyList<string> MissingRecommendedPackages,
    bool PumaSecurityRulesPresent = false,
    IReadOnlyList<string>? SecurityRelatedNuGetPackages = null);
