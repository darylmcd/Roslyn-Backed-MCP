using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompileCheckService : ICompileCheckService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CompileCheckService> _logger;

    public CompileCheckService(IWorkspaceManager workspace, ILogger<CompileCheckService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<CompileCheckDto> CheckAsync(
        string workspaceId,
        CompileCheckOptions options,
        CancellationToken ct)
    {
        var (projectFilter, emitValidation, severityFilter, fileFilter, offset, limit) = options;
        var sw = Stopwatch.StartNew();
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);
        var allDiagnostics = new List<DiagnosticDto>();
        int errorCount = 0;
        int warningCount = 0;

        var minSeverity = ParseMinimumSeverity(severityFilter);
        var normalizedFileFilter = string.IsNullOrWhiteSpace(fileFilter)
            ? null
            : Path.GetFullPath(fileFilter);

        var projectList = projects.ToList();
        int completedProjects = 0;
        bool cancelled = false;

        try
        {
            foreach (var project in projectList)
            {
                ct.ThrowIfCancellationRequested();

                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation is null) { completedProjects++; continue; }

                IEnumerable<Diagnostic> diagnostics;

                if (emitValidation)
                {
                    var emitOptions = new EmitOptions(metadataOnly: false);
                    using var stream = new MemoryStream();
                    var emitResult = compilation.Emit(stream, options: emitOptions, cancellationToken: ct);
                    diagnostics = emitResult.Diagnostics;
                }
                else
                {
                    diagnostics = compilation.GetDiagnostics(ct);
                }

                foreach (var diag in diagnostics)
                {
                    if (diag.Severity == DiagnosticSeverity.Hidden) continue;
                    if (minSeverity is not null && diag.Severity < minSeverity) continue;

                    var lineSpan = diag.Location.GetMappedLineSpan();
                    if (normalizedFileFilter is not null)
                    {
                        var diagPath = lineSpan.Path;
                        if (string.IsNullOrEmpty(diagPath)) continue;
                        if (!Path.GetFullPath(diagPath).Equals(normalizedFileFilter, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (diag.Severity == DiagnosticSeverity.Error) errorCount++;
                    else if (diag.Severity == DiagnosticSeverity.Warning) warningCount++;

                    allDiagnostics.Add(new DiagnosticDto(
                        Id: diag.Id,
                        Message: diag.GetMessage(),
                        Severity: diag.Severity.ToString(),
                        Category: diag.Descriptor.Category,
                        FilePath: lineSpan.Path,
                        StartLine: lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : null,
                        StartColumn: lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : null,
                        EndLine: lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : null,
                        EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null));
                }

                completedProjects++;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            _logger.LogWarning(
                "compile_check cancelled after {Completed}/{Total} projects",
                completedProjects, projectList.Count);
        }

        var pagedDiagnostics = allDiagnostics.Skip(offset).Take(limit).ToList();
        sw.Stop();

        // Heuristic: many CS0234 ("type or namespace not found") errors across the solution
        // usually indicate NuGet packages haven't been restored.
        var cs0234Count = allDiagnostics.Count(d => d.Id == "CS0234");
        var restoreHint = cs0234Count >= 10
            ? $"Detected {cs0234Count} CS0234 (type or namespace not found) errors. " +
              "This usually means NuGet packages are not restored. " +
              "Run 'dotnet restore' on the solution, then call workspace_reload."
            : null;

        return new CompileCheckDto(
            Success: errorCount == 0 && !cancelled,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            TotalDiagnostics: allDiagnostics.Count,
            ReturnedDiagnostics: pagedDiagnostics.Count,
            Offset: offset,
            Limit: limit,
            HasMore: offset + pagedDiagnostics.Count < allDiagnostics.Count,
            Diagnostics: pagedDiagnostics,
            ElapsedMs: sw.ElapsedMilliseconds,
            RestoreHint: restoreHint,
            Cancelled: cancelled,
            CompletedProjects: completedProjects,
            TotalProjects: projectList.Count);
    }

    private static DiagnosticSeverity? ParseMinimumSeverity(string? severityFilter)
    {
        if (string.IsNullOrWhiteSpace(severityFilter)) return null;
        return severityFilter.Trim().ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => null
        };
    }
}
