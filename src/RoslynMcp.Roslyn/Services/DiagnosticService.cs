using System.Collections.Immutable;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

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
        // Default to Warning severity when no filter specified to avoid multi-MB Hidden output
        DiagnosticSeverity? minSeverity = ParseSeverity(severityFilter) ?? DiagnosticSeverity.Warning;

        var rawWorkspace = (await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false)).WorkspaceDiagnostics;
        var workspaceDiagnostics = FilterDiagnostics(
            NormalizeWorkspaceDiagnostics(rawWorkspace),
            fileFilter,
            minSeverity: minSeverity);

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

        var compilerErrors = compilerDiagnostics.Count(d => d.Severity == "Error");
        var analyzerErrors = analyzerDiagnostics.Count(d => d.Severity == "Error");
        var workspaceErrors = workspaceDiagnostics.Count(d => d.Severity == "Error");

        return new DiagnosticsResultDto(
            workspaceDiagnostics,
            compilerDiagnostics,
            analyzerDiagnostics,
            TotalErrors: compilerErrors + analyzerErrors + workspaceErrors,
            TotalWarnings: compilerDiagnostics.Count(d => d.Severity == "Warning")
                + analyzerDiagnostics.Count(d => d.Severity == "Warning")
                + workspaceDiagnostics.Count(d => d.Severity == "Warning"),
            TotalInfo: compilerDiagnostics.Count(d => d.Severity == "Info")
                + analyzerDiagnostics.Count(d => d.Severity == "Info")
                + workspaceDiagnostics.Count(d => d.Severity == "Info"),
            CompilerErrors: compilerErrors,
            AnalyzerErrors: analyzerErrors,
            WorkspaceErrors: workspaceErrors);
    }

    /// <summary>
    /// Downgrades known informational SDK/workspace messages that MSBuildWorkspace surfaces as failures.
    /// </summary>
    private static IReadOnlyList<DiagnosticDto> NormalizeWorkspaceDiagnostics(IReadOnlyList<DiagnosticDto> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return diagnostics;
        }

        var list = new List<DiagnosticDto>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Category, "Workspace", StringComparison.OrdinalIgnoreCase) &&
                IsInformationalWorkspaceFailureMessage(d.Message))
            {
                list.Add(d with { Severity = "Warning" });
            }
            else
            {
                list.Add(d);
            }
        }

        return list;
    }

    private static bool IsInformationalWorkspaceFailureMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        // SDK package pruning hints (e.g. "PackageReference X will not be pruned...")
        if (message.Contains("pruned", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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
            Description: BuildDiagnosticDescription(diagnostic),
            HelpLinkUri: BuildHelpLink(diagnostic.Id),
            SupportedFixes: GetSupportedFixes(diagnostic.Id));
    }

    private static string BuildDiagnosticDescription(Diagnostic diagnostic)
    {
        var fromDesc = diagnostic.Descriptor.Description.ToString();
        if (!string.IsNullOrWhiteSpace(fromDesc))
            return fromDesc;

        var fromFormat = diagnostic.Descriptor.MessageFormat.ToString();
        if (!string.IsNullOrWhiteSpace(fromFormat))
            return fromFormat;

        return diagnostic.GetMessage();
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
        diagnosticId.ToUpperInvariant() switch
        {
            "CS8019" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_using",
                    Title: "Remove unused using",
                    Description: "Deletes the using directive flagged as unnecessary.")
            ],
            "IDE0005" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_using",
                    Title: "Remove unnecessary using directive",
                    Description: "IDE-style unused using removal (requires a matching code fix provider in the workspace).")
            ],
            "CS0414" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_field",
                    Title: "Remove unused field",
                    Description: "Field is assigned but never read; remove or use the field.")
            ],
            "CS8600" or "CS8602" or "CS8603" =>
            [
                new CodeFixOptionDto(
                    FixId: "nullable_fix",
                    Title: "Address nullable reference warning",
                    Description: "Add null check, use null-forgiving operator, or adjust annotations.")
            ],
            "CA2234" =>
            [
                new CodeFixOptionDto(
                    FixId: "pass_system_uri",
                    Title: "Pass System.Uri instead of string",
                    Description: "CA2234: use Uri overload for HTTP client calls where applicable.")
            ],
            _ => []
        };

    private static bool DiagnosticMatchesLocation(Diagnostic diagnostic, string diagnosticId, string filePath, int line, int column)
    {
        if (!string.Equals(diagnostic.Id, diagnosticId, StringComparison.OrdinalIgnoreCase) ||
            !diagnostic.Location.IsInSource)
            return false;

        var lineSpan = diagnostic.Location.GetLineSpan();
        return string.Equals(Path.GetFullPath(lineSpan.Path), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase) &&
               lineSpan.StartLinePosition.Line + 1 == line &&
               lineSpan.StartLinePosition.Character + 1 == column;
    }

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
                continue;

            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                if (DiagnosticMatchesLocation(diagnostic, diagnosticId, filePath, line, column))
                    return diagnostic;
            }

            var analyzers = project.AnalyzerReferences
                .SelectMany(reference => reference.GetAnalyzers(project.Language))
                .ToImmutableArray();
            if (analyzers.Length == 0)
                continue;

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                analyzers,
                new CompilationWithAnalyzersOptions(
                    options: project.AnalyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            var analyzerDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync(ct).ConfigureAwait(false);
            foreach (var diagnostic in analyzerDiagnostics)
            {
                if (DiagnosticMatchesLocation(diagnostic, diagnosticId, filePath, line, column))
                    return diagnostic;
            }
        }

        return null;
    }
}
