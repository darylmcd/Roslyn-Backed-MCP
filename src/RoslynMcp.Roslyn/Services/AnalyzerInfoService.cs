using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class AnalyzerInfoService : IAnalyzerInfoService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<AnalyzerInfoService> _logger;

    public AnalyzerInfoService(IWorkspaceManager workspace, ILogger<AnalyzerInfoService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AnalyzerInfoDto>> ListAnalyzersAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        // Collect all unique analyzers across projects
        var analyzersByAssembly = new Dictionary<string, List<AnalyzerRuleDto>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Get analyzer references from the project
            foreach (var analyzerRef in project.AnalyzerReferences)
            {
                var assemblyName = analyzerRef.Display ?? analyzerRef.FullPath ?? "Unknown";

                if (analyzersByAssembly.ContainsKey(assemblyName))
                    continue; // Already processed this assembly

                var rules = new List<AnalyzerRuleDto>();

                try
                {
                    var analyzers = analyzerRef.GetAnalyzers(compilation.Language);
                    foreach (var analyzer in analyzers)
                    {
                        foreach (var descriptor in analyzer.SupportedDiagnostics)
                        {
                            rules.Add(new AnalyzerRuleDto(
                                Id: descriptor.Id,
                                Title: descriptor.Title.ToString(),
                                Category: descriptor.Category,
                                DefaultSeverity: descriptor.DefaultSeverity.ToString(),
                                IsEnabledByDefault: descriptor.IsEnabledByDefault,
                                HelpLinkUri: descriptor.HelpLinkUri));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load analyzers from {Assembly}", assemblyName);
                    rules.Add(new AnalyzerRuleDto(
                        Id: "LOAD_ERROR",
                        Title: $"Failed to load: {ex.Message}",
                        Category: "Error",
                        DefaultSeverity: "Error",
                        IsEnabledByDefault: false,
                        HelpLinkUri: null));
                }

                analyzersByAssembly[assemblyName] = rules;
            }
        }

        return analyzersByAssembly
            .Select(kvp => new AnalyzerInfoDto(
                AssemblyName: kvp.Key,
                Rules: kvp.Value
                    .DistinctBy(r => r.Id)
                    .OrderBy(r => r.Id)
                    .ToList()))
            .OrderBy(a => a.AssemblyName)
            .ToList();
    }
}
