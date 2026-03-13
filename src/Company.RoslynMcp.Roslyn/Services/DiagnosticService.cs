using System.Collections.Immutable;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
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
        string workspaceId, string? projectFilter, string? fileFilter, string? severityFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var workspaceDiagnostics = FilterDiagnostics(
            _workspace.GetStatus(workspaceId).WorkspaceDiagnostics,
            fileFilter,
            minSeverity: ParseSeverity(severityFilter));
        var compilerDiagnostics = new List<DiagnosticDto>();
        var analyzerDiagnostics = new List<DiagnosticDto>();

        DiagnosticSeverity? minSeverity = ParseSeverity(severityFilter);

        foreach (var project in solution.Projects)
        {
            if (projectFilter is not null &&
                !string.Equals(project.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null)
            {
                compilerDiagnostics.Add(new DiagnosticDto(
                    "WORKSPACE001", $"Could not get compilation for project '{project.Name}'",
                    "Error", "Workspace", project.FilePath, null, null, null, null));
                continue;
            }

            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                if (MatchesFilter(diagnostic, fileFilter, minSeverity))
                {
                    compilerDiagnostics.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
                }
            }

            var analyzers = project.AnalyzerReferences
                .SelectMany(reference => reference.GetAnalyzers(project.Language))
                .ToImmutableArray();
            if (analyzers.Length == 0)
            {
                continue;
            }

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                analyzers,
                new CompilationWithAnalyzersOptions(
                    options: project.AnalyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            var projectAnalyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);
            foreach (var diagnostic in projectAnalyzerDiagnostics)
            {
                if (MatchesFilter(diagnostic, fileFilter, minSeverity))
                {
                    analyzerDiagnostics.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
                }
            }
        }

        return new DiagnosticsResultDto(
            workspaceDiagnostics,
            compilerDiagnostics,
            analyzerDiagnostics,
            TotalErrors: compilerDiagnostics.Count(d => d.Severity == "Error")
                + analyzerDiagnostics.Count(d => d.Severity == "Error")
                + workspaceDiagnostics.Count(d => d.Severity == "Error"),
            TotalWarnings: compilerDiagnostics.Count(d => d.Severity == "Warning")
                + analyzerDiagnostics.Count(d => d.Severity == "Warning")
                + workspaceDiagnostics.Count(d => d.Severity == "Warning"),
            TotalInfo: compilerDiagnostics.Count(d => d.Severity == "Info")
                + analyzerDiagnostics.Count(d => d.Severity == "Info")
                + workspaceDiagnostics.Count(d => d.Severity == "Info"));
    }

    public async Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var diagnostic = await FindDiagnosticAsync(solution, diagnosticId, filePath, line, column, ct);
        if (diagnostic is null)
        {
            return null;
        }

        return new DiagnosticDetailsDto(
            Diagnostic: SymbolMapper.ToDiagnosticDto(diagnostic),
            Description: diagnostic.Descriptor.Description.ToString(),
            HelpLinkUri: BuildHelpLink(diagnostic.Id),
            SupportedFixes: GetSupportedFixes(diagnostic.Id));
    }

    private static DiagnosticSeverity? ParseSeverity(string? severityFilter) =>
        severityFilter?.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => null
        };

    private static bool MatchesFilter(Diagnostic diagnostic, string? fileFilter, DiagnosticSeverity? minSeverity)
    {
        if (minSeverity.HasValue && diagnostic.Severity < minSeverity.Value)
        {
            return false;
        }

        if (fileFilter is not null && diagnostic.Location.IsInSource)
        {
            var diagnosticPath = diagnostic.Location.GetLineSpan().Path;
            if (!string.Equals(Path.GetFullPath(diagnosticPath), Path.GetFullPath(fileFilter), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<DiagnosticDto> FilterDiagnostics(
        IReadOnlyList<DiagnosticDto> diagnostics,
        string? fileFilter,
        DiagnosticSeverity? minSeverity)
    {
        return diagnostics
            .Where(diagnostic =>
            {
                if (minSeverity.HasValue &&
                    Enum.TryParse<DiagnosticSeverity>(diagnostic.Severity, ignoreCase: true, out var severity) &&
                    severity < minSeverity.Value)
                {
                    return false;
                }

                if (fileFilter is not null && diagnostic.FilePath is not null)
                {
                    return string.Equals(
                        Path.GetFullPath(diagnostic.FilePath),
                        Path.GetFullPath(fileFilter),
                        StringComparison.OrdinalIgnoreCase);
                }

                return true;
            })
            .ToList();
    }

    private static string BuildHelpLink(string diagnosticId) =>
        $"https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/{diagnosticId.ToLowerInvariant()}";

    private static IReadOnlyList<CodeFixOptionDto> GetSupportedFixes(string diagnosticId) =>
        diagnosticId switch
        {
            "CS8019" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_using",
                    Title: "Remove unused using",
                    Description: "Deletes the using directive flagged as unnecessary.")
            ],
            _ => []
        };

    private static async Task<Diagnostic?> FindDiagnosticAsync(
        Solution solution,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null)
            {
                continue;
            }

            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                if (!string.Equals(diagnostic.Id, diagnosticId, StringComparison.OrdinalIgnoreCase) ||
                    !diagnostic.Location.IsInSource)
                {
                    continue;
                }

                var lineSpan = diagnostic.Location.GetLineSpan();
                if (string.Equals(Path.GetFullPath(lineSpan.Path), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase) &&
                    lineSpan.StartLinePosition.Line + 1 == line &&
                    lineSpan.StartLinePosition.Character + 1 == column)
                {
                    return diagnostic;
                }
            }
        }

        return null;
    }
}
