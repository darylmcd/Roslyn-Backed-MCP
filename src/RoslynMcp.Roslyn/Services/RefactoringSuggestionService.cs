using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Aggregates complexity, cohesion, and dead-code metrics into ranked
/// refactoring suggestions with recommended tool sequences.
/// </summary>
public sealed class RefactoringSuggestionService : IRefactoringSuggestionService
{
    private readonly ICodeMetricsService _metricsService;
    private readonly IWorkspaceManager _workspace;
    private readonly ICohesionAnalysisService _cohesionService;
    private readonly IUnusedCodeAnalyzer _unusedCodeAnalyzer;
    private readonly ILogger<RefactoringSuggestionService> _logger;

    public RefactoringSuggestionService(
        IWorkspaceManager workspace,
        ICodeMetricsService metricsService,
        ICohesionAnalysisService cohesionService,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        ILogger<RefactoringSuggestionService> logger)
    {
        _workspace = workspace;
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
        // Parameter-count candidates can have low complexity and must survive metric pagination.
        var complexityTask = _metricsService.GetComplexityMetricsAsync(
            workspaceId, filePath: null, filePaths: null, projectFilter, minComplexity: null, limit: int.MaxValue, ct);
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
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var parameterTargets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var metric in complexityResults.Where(m => m.ParameterCount >= 7))
        {
            var measuredMethod = await ResolveMeasuredMethodAsync(solution, metric, ct).ConfigureAwait(false);
            var target = measuredMethod is null ? null : SymbolServiceHelpers.GetMethodDefinition(measuredMethod);
            if (target is not null && !parameterTargets.Add(target)) continue;

            var m = target is null ? metric : WithImplementationLocation(metric, target, ct);
            var supportsParameterObject = target is not null
                && await ParameterObjectService.HasViableParameterGroupAsync(solution, target, ct).ConfigureAwait(false);
            var guidance = supportsParameterObject
                ? "Consider introducing a parameter object."
                : "Review the declaration and dispatch contract, then introduce a parameter object manually.";
            suggestions.Add(new RefactoringSuggestionDto(
                Severity: "medium",
                Category: "parameter-count",
                Description: $"Method '{m.SymbolName}' has {m.ParameterCount} parameters. {guidance}",
                TargetSymbol: m.SymbolName,
                FilePath: m.FilePath,
                Line: m.Line,
                RecommendedTools: supportsParameterObject
                    ? ["parameter_object_preview", "apply_with_verify", "compile_check"]
                    : ["compile_check"]));
        }

        // Low cohesion (LCOM4 >= 2) → extract type or split class.
        // Suppress for any type carrying a LifecyclePattern: those patterns are expected to have
        // high LCOM4 by design (e.g., "action-triad", "facade"), so the split suggestion would be
        // a false-positive. Tracks suggest-refactorings-facade-extraction-false-positive.
        foreach (var c in cohesionResults.Where(c => c.Lcom4Score >= 2 && c.FilePath is not null && c.LifecyclePattern is null))
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

        // Sort by severity, then put complexity suggestions first, retaining insertion order within ties.
        var sorted = suggestions
            .OrderBy(s => s.Severity switch { "high" => 0, "medium" => 1, "low" => 2, _ => 3 })
            .ThenByDescending(s => s.Category == "complexity" ? 1 : 0)
            .Take(limit)
            .ToList();

        _logger.LogDebug("Generated {Count} refactoring suggestions for workspace {WorkspaceId}", sorted.Count, workspaceId);

        return sorted;
    }

    private static async Task<IMethodSymbol?> ResolveMeasuredMethodAsync(
        Solution solution, ComplexityMetricsDto metric, CancellationToken ct)
    {
        var candidates = new List<IMethodSymbol>();
        foreach (var document in solution.Projects.SelectMany(project => project.Documents)
            .Where(document => FileSystemPath.Comparer.Equals(document.FilePath, metric.FilePath)))
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || model is null) continue;

            foreach (var declaration in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>()
                .Where(declaration => declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == metric.Line))
            {
                if (model.GetDeclaredSymbol(declaration, ct) is IMethodSymbol method
                    && method.Name == metric.SymbolName && method.Parameters.Length == metric.ParameterCount)
                    candidates.Add(method);
            }
        }

        // Same-line overloads and linked documents can be ambiguous; never infer eligibility
        // from a neighboring declaration or publish an automated workflow without a unique target.
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static ComplexityMetricsDto WithImplementationLocation(
        ComplexityMetricsDto metric, IMethodSymbol method, CancellationToken ct)
    {
        var implementation = method.PartialImplementationPart ?? method;
        var declaration = implementation.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
        if (declaration is null) return metric;

        var lineSpan = declaration.GetLocation().GetLineSpan();
        return metric with { FilePath = lineSpan.Path, Line = lineSpan.StartLinePosition.Line + 1 };
    }
}
