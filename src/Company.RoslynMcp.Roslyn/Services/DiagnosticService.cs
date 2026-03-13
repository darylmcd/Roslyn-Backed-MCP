using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class DiagnosticService : IDiagnosticService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<DiagnosticService> _logger;

    public DiagnosticService(IWorkspaceManager workspace, ILogger<DiagnosticService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string? projectFilter, string? fileFilter, string? severityFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var workspaceDiagnostics = new List<DiagnosticDto>();
        var compilerDiagnostics = new List<DiagnosticDto>();

        DiagnosticSeverity? minSeverity = severityFilter?.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => null
        };

        foreach (var project in solution.Projects)
        {
            if (projectFilter is not null &&
                !string.Equals(project.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null)
            {
                workspaceDiagnostics.Add(new DiagnosticDto(
                    "WORKSPACE001", $"Could not get compilation for project '{project.Name}'",
                    "Error", "Workspace", project.FilePath, null, null, null, null));
                continue;
            }

            var diagnostics = compilation.GetDiagnostics(ct);
            foreach (var diagnostic in diagnostics)
            {
                if (minSeverity.HasValue && diagnostic.Severity < minSeverity.Value)
                    continue;

                if (fileFilter is not null && diagnostic.Location.IsInSource)
                {
                    var diagPath = diagnostic.Location.GetLineSpan().Path;
                    if (!string.Equals(Path.GetFullPath(diagPath), Path.GetFullPath(fileFilter), StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                compilerDiagnostics.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
            }
        }

        return new DiagnosticsResultDto(
            workspaceDiagnostics,
            compilerDiagnostics,
            TotalErrors: compilerDiagnostics.Count(d => d.Severity == "Error") + workspaceDiagnostics.Count(d => d.Severity == "Error"),
            TotalWarnings: compilerDiagnostics.Count(d => d.Severity == "Warning"),
            TotalInfo: compilerDiagnostics.Count(d => d.Severity == "Info"));
    }
}
