using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 9 implementation. Runs three sub-queries in parallel:
/// <list type="number">
///   <item><description>Cross-solution references for the target symbol.</description></item>
///   <item><description>Switch-exhaustiveness diagnostics (CS8509, CS8524, IDE0072) project-wide.</description></item>
///   <item><description>Callsites where the enclosing type name matches a mapper/converter/serializer suffix (heuristic).</description></item>
/// </list>
/// Emits a structured <see cref="SymbolImpactSweepDto"/> so downstream tooling can surface the
/// grouped results as a review checklist without separately hitting each underlying tool.
/// </summary>
public sealed class ImpactSweepService : IImpactSweepService
{
    private static readonly string[] SwitchExhaustivenessIds = ["CS8509", "CS8524", "IDE0072"];
    private static readonly string[] MapperSuffixes = ["Mapper", "Converter", "Serializer", "Translator", "Adapter"];

    private readonly IWorkspaceManager _workspace;
    private readonly IReferenceService _references;
    private readonly IDiagnosticService _diagnostics;

    public ImpactSweepService(IWorkspaceManager workspace, IReferenceService references, IDiagnosticService diagnostics)
    {
        _workspace = workspace;
        _references = references;
        _diagnostics = diagnostics;
    }

    public async Task<SymbolImpactSweepDto> SweepAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        locator.Validate();

        // Run the three independent queries in parallel. The references query blocks on the
        // workspace read side; diagnostics runs against the analyzer pipeline.
        var referencesTask = _references.FindReferencesAsync(workspaceId, locator, ct);
        var diagnosticsTask = CollectSwitchExhaustivenessDiagnosticsAsync(workspaceId, ct);

        var references = await referencesTask.ConfigureAwait(false);
        var diagnostics = await diagnosticsTask.ConfigureAwait(false);

        // Mapper callsites are a post-filter over references — only keep those whose file path
        // or enclosing type name contains a mapper suffix.
        var mapperCallsites = FilterMapperCallsites(references);

        // Resolve the symbol display name for the response header.
        var symbolDisplay = await ResolveSymbolDisplayAsync(workspaceId, locator, ct).ConfigureAwait(false);

        var tasks = BuildSuggestedTasks(references.Count, diagnostics.Count, mapperCallsites.Count);

        return new SymbolImpactSweepDto(
            SymbolDisplay: symbolDisplay,
            References: references,
            SwitchExhaustivenessIssues: diagnostics,
            MapperCallsites: mapperCallsites,
            SuggestedTasks: tasks);
    }

    private async Task<IReadOnlyList<DiagnosticDto>> CollectSwitchExhaustivenessDiagnosticsAsync(
        string workspaceId, CancellationToken ct)
    {
        var hits = new List<DiagnosticDto>();
        foreach (var id in SwitchExhaustivenessIds)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _diagnostics
                .GetDiagnosticsAsync(workspaceId, projectFilter: null, fileFilter: null,
                    severityFilter: "Info", diagnosticIdFilter: id, ct)
                .ConfigureAwait(false);

            // We don't distinguish compiler vs analyzer here — a CS8509 is the same concern
            // whether it came from the compiler layer or an IDE analyzer.
            hits.AddRange(result.CompilerDiagnostics);
            hits.AddRange(result.AnalyzerDiagnostics);
        }
        return hits;
    }

    private static IReadOnlyList<LocationDto> FilterMapperCallsites(IReadOnlyList<LocationDto> references)
    {
        if (references.Count == 0) return Array.Empty<LocationDto>();
        var mapperHits = new List<LocationDto>();
        foreach (var loc in references)
        {
            var fileName = Path.GetFileNameWithoutExtension(loc.FilePath);
            if (string.IsNullOrEmpty(fileName)) continue;
            foreach (var suffix in MapperSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    mapperHits.Add(loc);
                    break;
                }
            }
        }
        return mapperHits;
    }

    private async Task<string> ResolveSymbolDisplayAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        return symbol?.ToDisplayString() ?? "<unresolved>";
    }

    private static IReadOnlyList<string> BuildSuggestedTasks(int refCount, int switchCount, int mapperCount)
    {
        var tasks = new List<string>();
        if (refCount > 0)
            tasks.Add($"Review {refCount} reference(s) — update call sites that assume the prior shape.");
        if (switchCount > 0)
            tasks.Add($"Fix {switchCount} non-exhaustive switch statement(s) flagged by CS8509/CS8524/IDE0072.");
        if (mapperCount > 0)
            tasks.Add($"Audit {mapperCount} mapper/converter callsite(s) for paired To*/From* updates.");
        if (tasks.Count == 0)
            tasks.Add("No impact detected. Consider running project_diagnostics to confirm.");
        return tasks;
    }
}
