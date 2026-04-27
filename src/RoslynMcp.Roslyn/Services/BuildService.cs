using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class BuildService : IBuildService
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IGatedCommandExecutor _executor;
    private readonly ILogger<BuildService> _logger;
    private readonly ValidationServiceOptions _options;

    public BuildService(
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<BuildService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
    }

    public async Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct)
    {
        var status = await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var targetPath = status.LoadedPath ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        var execution = await _executor.ExecuteAsync(
            workspaceId,
            targetPath,
            ["build", targetPath, "--nologo"],
            _options.BuildTimeout,
            ct).ConfigureAwait(false);
        var diagnostics = await EnrichDiagnosticSpansAsync(
            workspaceId,
            DotnetOutputParser.ParseBuildDiagnostics($"{execution.StdOut}{Environment.NewLine}{execution.StdErr}"),
            ct).ConfigureAwait(false);

        return new BuildResultDto(
            execution,
            diagnostics,
            diagnostics.Count(d => d.Severity == "Error"),
            diagnostics.Count(d => d.Severity == "Warning"));
    }

    public async Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct)
    {
        var project = _executor.ResolveProject(workspaceId, projectName);
        var execution = await _executor.ExecuteAsync(
            workspaceId,
            project.FilePath,
            ["build", project.FilePath, "--nologo"],
            _options.BuildTimeout,
            ct).ConfigureAwait(false);
        var diagnostics = await EnrichDiagnosticSpansAsync(
            workspaceId,
            DotnetOutputParser.ParseBuildDiagnostics($"{execution.StdOut}{Environment.NewLine}{execution.StdErr}"),
            ct).ConfigureAwait(false);

        return new BuildResultDto(
            execution,
            diagnostics,
            diagnostics.Count(d => d.Severity == "Error"),
            diagnostics.Count(d => d.Severity == "Warning"));
    }

    private async Task<IReadOnlyList<DiagnosticDto>> EnrichDiagnosticSpansAsync(
        string workspaceId,
        IReadOnlyList<DiagnosticDto> diagnostics,
        CancellationToken ct)
    {
        var candidates = diagnostics
            .Where(diagnostic => diagnostic is
            {
                FilePath.Length: > 0,
                StartLine: not null,
                StartColumn: not null,
                EndLine: null
            } or
            {
                FilePath.Length: > 0,
                StartLine: not null,
                StartColumn: not null,
                EndColumn: null
            })
            .ToList();
        if (candidates.Count == 0)
        {
            return diagnostics;
        }

        Dictionary<string, (int EndLine, int EndColumn)> spans;
        try
        {
            var solution = _workspaceManager.GetCurrentSolution(workspaceId);
            spans = await CollectRoslynDiagnosticSpansAsync(solution, candidates, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to enrich build diagnostic spans for workspace {WorkspaceId}", workspaceId);
            return diagnostics;
        }

        if (spans.Count == 0)
        {
            return diagnostics;
        }

        return diagnostics
            .Select(diagnostic =>
            {
                var key = BuildDiagnosticSpanKey(
                    diagnostic.Id,
                    diagnostic.FilePath,
                    diagnostic.StartLine,
                    diagnostic.StartColumn);
                return key is not null && spans.TryGetValue(key, out var span)
                    ? diagnostic with { EndLine = span.EndLine, EndColumn = span.EndColumn }
                    : diagnostic;
            })
            .ToList();
    }

    private static async Task<Dictionary<string, (int EndLine, int EndColumn)>> CollectRoslynDiagnosticSpansAsync(
        Microsoft.CodeAnalysis.Solution solution,
        IReadOnlyList<DiagnosticDto> candidates,
        CancellationToken ct)
    {
        var spans = new Dictionary<string, (int EndLine, int EndColumn)>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in candidates
            .Select(candidate => candidate.FilePath)
            .Where(static filePath => !string.IsNullOrWhiteSpace(filePath))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var document in FindDocuments(solution, filePath))
            {
                ct.ThrowIfCancellationRequested();
                var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (syntaxTree is null)
                {
                    continue;
                }

                var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation is null)
                {
                    continue;
                }

                foreach (var diagnostic in compilation.GetDiagnostics(ct))
                {
                    if (diagnostic.Location.SourceTree != syntaxTree || !diagnostic.Location.IsInSource)
                    {
                        continue;
                    }

                    var lineSpan = diagnostic.Location.GetLineSpan();
                    if (!lineSpan.IsValid)
                    {
                        continue;
                    }

                    var key = BuildDiagnosticSpanKey(
                        diagnostic.Id,
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.StartLinePosition.Character + 1);
                    if (key is null)
                    {
                        continue;
                    }

                    spans[key] = (
                        lineSpan.EndLinePosition.Line + 1,
                        lineSpan.EndLinePosition.Character + 1);
                }
            }
        }

        return spans;
    }

    private static IEnumerable<Document> FindDocuments(Microsoft.CodeAnalysis.Solution solution, string filePath)
    {
        var documentIds = solution.GetDocumentIdsWithFilePath(filePath);
        if (!documentIds.IsDefaultOrEmpty)
        {
            foreach (var documentId in documentIds)
            {
                var document = solution.GetDocument(documentId);
                if (document is not null)
                {
                    yield return document;
                }
            }
            yield break;
        }

        var normalized = NormalizePath(filePath);
        if (normalized is null)
        {
            yield break;
        }

        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            if (document.FilePath is not null &&
                string.Equals(NormalizePath(document.FilePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                yield return document;
            }
        }
    }

    private static string? BuildDiagnosticSpanKey(string id, string? filePath, int? line, int? column)
    {
        var normalizedPath = NormalizePath(filePath);
        return normalizedPath is null || line is null || column is null
            ? null
            : $"{id}\0{normalizedPath}\0{line.Value}\0{column.Value}";
    }

    private static string? NormalizePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return filePath;
        }
    }
}
