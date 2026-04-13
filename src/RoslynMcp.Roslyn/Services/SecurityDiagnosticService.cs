using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Filters and enriches the existing diagnostic pipeline with security-focused metadata.
/// Does not reimplement diagnostic collection — delegates to <see cref="IDiagnosticService"/>.
/// </summary>
public sealed class SecurityDiagnosticService : ISecurityDiagnosticService
{
    private readonly IDiagnosticService _diagnosticService;
    private readonly IWorkspaceManager _workspace;
    private readonly IMsBuildEvaluationService _msBuildEvaluation;
    private readonly ILogger<SecurityDiagnosticService> _logger;

    /// <summary>
    /// Recommended security analyzer NuGet packages beyond what the SDK provides.
    /// </summary>
    private static readonly string[] RecommendedPackages =
    [
        "SecurityCodeScan.VS2019"
    ];

    /// <summary>
    /// Known assembly name prefixes that indicate a security analyzer.
    /// </summary>
    private static readonly string[] SecurityCodeScanAssemblyPrefixes =
    [
        "SecurityCodeScan"
    ];

    private static readonly string[] PumaSecurityAssemblyPrefixes =
    [
        "Puma."
    ];

    private static readonly string[] NetAnalyzerAssemblyPrefixes =
    [
        "Microsoft.CodeAnalysis.NetAnalyzers",
        "Microsoft.CodeAnalysis.CSharp.NetAnalyzers",
        "Microsoft.CodeQuality.Analyzers",
        "Microsoft.NetCore.Analyzers",
        "Microsoft.NetFramework.Analyzers"
    ];

    public SecurityDiagnosticService(
        IDiagnosticService diagnosticService,
        IWorkspaceManager workspace,
        IMsBuildEvaluationService msBuildEvaluation,
        ILogger<SecurityDiagnosticService> logger)
    {
        _diagnosticService = diagnosticService;
        _workspace = workspace;
        _msBuildEvaluation = msBuildEvaluation;
        _logger = logger;
    }

    public async Task<SecurityDiagnosticsResultDto> GetSecurityDiagnosticsAsync(
        string workspaceId, string? projectFilter, string? fileFilter, CancellationToken ct)
    {
        var diagnostics = await _diagnosticService.GetDiagnosticsAsync(workspaceId, projectFilter, fileFilter, null, null, ct)
            .ConfigureAwait(false);

        var findings = new List<SecurityDiagnosticDto>();

        foreach (var diag in diagnostics.CompilerDiagnostics.Concat(diagnostics.AnalyzerDiagnostics))
        {
            var info = SecurityDiagnosticRegistry.GetSecurityInfo(diag.Id);
            if (info is null)
            {
                continue;
            }

            findings.Add(new SecurityDiagnosticDto(
                DiagnosticId: diag.Id,
                Message: diag.Message,
                SecurityCategory: info.SecurityCategory,
                OwaspCategory: info.OwaspCategory,
                SecuritySeverity: info.SecuritySeverity,
                FilePath: diag.FilePath,
                StartLine: diag.StartLine,
                StartColumn: diag.StartColumn,
                FixHint: info.FixHint,
                HelpLinkUri: BuildHelpLink(diag.Id)));
        }

        // Sort by severity (Critical first) then by file path
        findings.Sort((a, b) =>
        {
            var severityOrder = GetSeverityOrder(a.SecuritySeverity).CompareTo(GetSeverityOrder(b.SecuritySeverity));
            if (severityOrder != 0) return severityOrder;
            return string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        });

        var analyzerStatus = await GetAnalyzerStatusAsync(workspaceId, ct).ConfigureAwait(false);

        return new SecurityDiagnosticsResultDto(
            Findings: findings,
            AnalyzerStatus: analyzerStatus,
            TotalFindings: findings.Count,
            CriticalCount: findings.Count(f => f.SecuritySeverity == "Critical"),
            HighCount: findings.Count(f => f.SecuritySeverity == "High"),
            MediumCount: findings.Count(f => f.SecuritySeverity == "Medium"),
            LowCount: findings.Count(f => f.SecuritySeverity == "Low"));
    }

    public async Task<SecurityAnalyzerStatusDto> GetAnalyzerStatusAsync(string workspaceId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var netAnalyzersPresent = false;
        var securityCodeScanFromAssembly = false;
        var pumaFromAssembly = false;

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var analyzerRef in project.AnalyzerReferences)
            {
                var displayName = analyzerRef.Display ?? string.Empty;

                if (!netAnalyzersPresent && MatchesAnyPrefix(displayName, NetAnalyzerAssemblyPrefixes))
                    netAnalyzersPresent = true;

                if (!securityCodeScanFromAssembly && MatchesAnyPrefix(displayName, SecurityCodeScanAssemblyPrefixes))
                    securityCodeScanFromAssembly = true;

                if (!pumaFromAssembly && MatchesAnyPrefix(displayName, PumaSecurityAssemblyPrefixes))
                    pumaFromAssembly = true;
            }
        }

        var nugetPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(project.FilePath))
                continue;

            try
            {
                var evaluated = await _msBuildEvaluation.EvaluateItemsAsync(
                    workspaceId, project.Name, "PackageReference", ct).ConfigureAwait(false);

                foreach (var item in evaluated.Items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Include))
                        nugetPackages.Add(item.Include);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not evaluate PackageReference items for project '{ProjectName}'", project.Name);
            }
        }

        var securityRelatedNuget = nugetPackages
            .Where(IsSecurityRelatedNuGetPackage)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var securityCodeScanPresent = securityCodeScanFromAssembly ||
            nugetPackages.Any(p => p.Contains("SecurityCodeScan", StringComparison.OrdinalIgnoreCase));

        var pumaPresent = pumaFromAssembly ||
            nugetPackages.Any(p => p.Contains("Puma.Security", StringComparison.OrdinalIgnoreCase));

        var missingPackages = new List<string>();
        if (!securityCodeScanPresent)
            missingPackages.AddRange(RecommendedPackages);

        return new SecurityAnalyzerStatusDto(
            NetAnalyzersPresent: netAnalyzersPresent,
            SecurityCodeScanPresent: securityCodeScanPresent,
            MissingRecommendedPackages: missingPackages,
            PumaSecurityRulesPresent: pumaPresent,
            SecurityRelatedNuGetPackages: securityRelatedNuget);
    }

    private static bool IsSecurityRelatedNuGetPackage(string packageId) =>
        packageId.Contains("SecurityCodeScan", StringComparison.OrdinalIgnoreCase) ||
        packageId.Contains("Puma.Security", StringComparison.OrdinalIgnoreCase) ||
        packageId.Contains("SonarAnalyzer", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAnyPrefix(string value, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static int GetSeverityOrder(string severity) =>
        severity switch
        {
            "Critical" => 0,
            "High" => 1,
            "Medium" => 2,
            "Low" => 3,
            _ => 4
        };

    private static string BuildHelpLink(string diagnosticId)
    {
        if (diagnosticId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/{diagnosticId.ToLowerInvariant()}";
        }

        if (diagnosticId.StartsWith("SCS", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://security-code-scan.github.io/#{diagnosticId}";
        }

        return $"https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/{diagnosticId.ToLowerInvariant()}";
    }
}
