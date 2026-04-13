using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Aggregates complexity, cohesion, and dead-code metrics into ranked
/// refactoring suggestions with recommended tool sequences.
/// </summary>
public sealed class RefactoringSuggestionService : IRefactoringSuggestionService
{
    private readonly ICodeMetricsService _metricsService;
    private readonly ICohesionAnalysisService _cohesionService;
    private readonly IUnusedCodeAnalyzer _unusedCodeAnalyzer;
    private readonly ILogger<RefactoringSuggestionService> _logger;

    public RefactoringSuggestionService(
        ICodeMetricsService metricsService,
        ICohesionAnalysisService cohesionService,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        ILogger<RefactoringSuggestionService> logger)
    {
        _metricsService = metricsService;
        _cohesionService = cohesionService;
        _unusedCodeAnalyzer = unusedCodeAnalyzer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RefactoringSuggestionDto>> SuggestRefactoringsAsync(
        string workspaceId, string? projectFilter, int limit, CancellationToken ct)
    {
        var suggestions = new List<RefactoringSuggestionDto>();

        // Run all analyses in parallel
        var complexityTask = _metricsService.GetComplexityMetricsAsync(
            workspaceId, filePath: null, projectFilter, minComplexity: 10, limit: 100, ct);
        var cohesionTask = _cohesionService.GetCohesionMetricsAsync(
            workspaceId, filePath: null, projectFilter, minMethods: 3, limit: 100,
            includeInterfaces: false, excludeTestProjects: true, ct);
        var unusedTask = _unusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId, new UnusedSymbolsAnalysisOptions
            {
                ProjectFilter = projectFilter,
                IncludePublic = false,
                ExcludeTests = true,
                ExcludeConventionInvoked = true,
                Limit = 50
            }, ct);

        await Task.WhenAll(complexityTask, cohesionTask, unusedTask).ConfigureAwait(false);

        var complexityResults = await complexityTask;
        var cohesionResults = await cohesionTask;
        var unusedResults = await unusedTask;

        // High complexity → extract method
        foreach (var m in complexityResults.Where(m => m.CyclomaticComplexity >= 15))
        {
            suggestions.Add(new RefactoringSuggestionDto(
                Severity: "high",
                Category: "complexity",
                Description: $"Method '{m.SymbolName}' has cyclomatic complexity {m.CyclomaticComplexity} (maintainability index {m.MaintainabilityIndex:F0}). Extract cohesive blocks into smaller methods.",
                TargetSymbol: m.SymbolName,
                FilePath: m.FilePath,
                Line: m.Line,
                RecommendedTools: ["analyze_data_flow", "extract_method_preview", "extract_method_apply", "compile_check"]));
        }

        // Moderate complexity → flag for review
        foreach (var m in complexityResults.Where(m => m.CyclomaticComplexity >= 10 && m.CyclomaticComplexity < 15))
        {
            suggestions.Add(new RefactoringSuggestionDto(
                Severity: "medium",
                Category: "complexity",
                Description: $"Method '{m.SymbolName}' has cyclomatic complexity {m.CyclomaticComplexity}. Consider extracting complex branches.",
                TargetSymbol: m.SymbolName,
                FilePath: m.FilePath,
                Line: m.Line,
                RecommendedTools: ["get_complexity_metrics", "analyze_data_flow", "extract_method_preview"]));
        }

        // High parameter count → introduce parameter object
        foreach (var m in complexityResults.Where(m => m.ParameterCount >= 7))
        {
            suggestions.Add(new RefactoringSuggestionDto(
                Severity: "medium",
                Category: "parameter-count",
                Description: $"Method '{m.SymbolName}' has {m.ParameterCount} parameters. Consider introducing a parameter object.",
                TargetSymbol: m.SymbolName,
                FilePath: m.FilePath,
                Line: m.Line,
                RecommendedTools: ["extract_type_preview", "extract_type_apply", "compile_check"]));
        }

        // Low cohesion (LCOM4 >= 2) → extract type or split class
        foreach (var c in cohesionResults.Where(c => c.Lcom4Score >= 2 && c.FilePath is not null))
        {
            suggestions.Add(new RefactoringSuggestionDto(
                Severity: c.Lcom4Score >= 3 ? "high" : "medium",
                Category: "cohesion",
                Description: $"Type '{c.TypeName}' has LCOM4 score {c.Lcom4Score} ({c.Clusters.Count} independent clusters, {c.MethodCount} methods). Split into cohesive types.",
                TargetSymbol: c.TypeName,
                FilePath: c.FilePath!,
                Line: c.Line ?? 0,
                RecommendedTools: ["get_cohesion_metrics", "find_shared_members", "extract_type_preview", "extract_type_apply", "compile_check"]));
        }

        // Unused private symbols (high confidence) → remove dead code
        foreach (var u in unusedResults.Where(u => string.Equals(u.Confidence, "high", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add(new RefactoringSuggestionDto(
                Severity: "low",
                Category: "dead-code",
                Description: $"Unused {u.SymbolKind.ToLowerInvariant()} '{u.SymbolName}' in {u.ContainingType ?? "global scope"}.",
                TargetSymbol: u.SymbolName,
                FilePath: u.FilePath,
                Line: u.Line,
                RecommendedTools: ["find_unused_symbols", "remove_dead_code_preview", "remove_dead_code_apply"]));
        }

        // Sort: high → medium → low, then by complexity descending
        var sorted = suggestions
            .OrderBy(s => s.Severity switch { "high" => 0, "medium" => 1, "low" => 2, _ => 3 })
            .ThenByDescending(s => s.Category == "complexity" ? 1 : 0)
            .Take(limit)
            .ToList();

        _logger.LogDebug("Generated {Count} refactoring suggestions for workspace {WorkspaceId}", sorted.Count, workspaceId);

        return sorted;
    }
}
