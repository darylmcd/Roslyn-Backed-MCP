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

        DiagnosticSeverity? minSeverity = ParseSeverity(severityFilter);

        var projects = solution.Projects
            .Where(p => projectFilter is null || string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var projectTasks = projects.Select(async project =>
        {
            var compiler = new List<DiagnosticDto>();
            var analyzer = new List<DiagnosticDto>();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                compiler.Add(new DiagnosticDto(
                    "WORKSPACE001", $"Could not get compilation for project '{project.Name}'",
                    "Error", "Workspace", project.FilePath, null, null, null, null));
                return (compiler, analyzer);
            }

            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                if (MatchesFilter(diagnostic, fileFilter, minSeverity))
                {
                    compiler.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
                }
            }

            var analyzers = project.AnalyzerReferences
                .SelectMany(reference => reference.GetAnalyzers(project.Language))
                .ToImmutableArray();
            if (analyzers.Length > 0)
            {
                var compilationWithAnalyzers = compilation.WithAnalyzers(
                    analyzers,
                    new CompilationWithAnalyzersOptions(
                        options: project.AnalyzerOptions,
                        onAnalyzerException: null,
                        concurrentAnalysis: true,
                        logAnalyzerExecutionTime: false,
                        reportSuppressedDiagnostics: false));

                var projectAnalyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
                foreach (var diagnostic in projectAnalyzerDiagnostics)
                {
                    if (MatchesFilter(diagnostic, fileFilter, minSeverity))
                    {
                        analyzer.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
                    }
                }
            }

            return (compiler, analyzer);
        });

        var results = await Task.WhenAll(projectTasks).ConfigureAwait(false);
        var compilerDiagnostics = results.SelectMany(r => r.compiler).ToList();
        var analyzerDiagnostics = results.SelectMany(r => r.analyzer).ToList();

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
        var diagnostic = await FindDiagnosticAsync(solution, diagnosticId, filePath, line, column, ct).ConfigureAwait(false);
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
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
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
