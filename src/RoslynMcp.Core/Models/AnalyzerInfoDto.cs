namespace RoslynMcp.Core.Models;

/// <summary>
/// Describes a loaded analyzer assembly and its supported diagnostic rules.
/// </summary>
public sealed record AnalyzerInfoDto(
    string AssemblyName,
    IReadOnlyList<AnalyzerRuleDto> Rules);

/// <summary>
/// Describes a single diagnostic rule supported by an analyzer.
/// </summary>
public sealed record AnalyzerRuleDto(
    string Id,
    string Title,
    string Category,
    string DefaultSeverity,
    bool IsEnabledByDefault,
    string? HelpLinkUri);
