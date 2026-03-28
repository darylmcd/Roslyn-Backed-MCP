using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
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
        string workspaceId, string? projectFilter, bool emitValidation, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);
        var allDiagnostics = new List<DiagnosticDto>();
        int errorCount = 0;
        int warningCount = 0;

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            IEnumerable<Diagnostic> diagnostics;

            if (emitValidation)
            {
                // Full emit validation catches more issues (e.g., missing references at emit time)
                using var stream = new MemoryStream();
                var emitResult = compilation.Emit(stream, cancellationToken: ct);
                diagnostics = emitResult.Diagnostics;
            }
            else
            {
                diagnostics = compilation.GetDiagnostics(ct);
            }

            foreach (var diag in diagnostics)
            {
                if (diag.Severity == DiagnosticSeverity.Hidden) continue;

                if (diag.Severity == DiagnosticSeverity.Error) errorCount++;
                else if (diag.Severity == DiagnosticSeverity.Warning) warningCount++;

                var lineSpan = diag.Location.GetMappedLineSpan();
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
        }

        sw.Stop();
        return new CompileCheckDto(
            Success: errorCount == 0,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            Diagnostics: allDiagnostics,
            ElapsedMs: sw.ElapsedMilliseconds);
    }
}
