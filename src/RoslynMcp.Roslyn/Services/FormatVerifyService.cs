using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// format-verify-solution-wide: runs <see cref="Formatter.FormatAsync(Document, OptionSet?, CancellationToken)"/>
/// over every document in the workspace (or in a single project filter), compares the result to
/// the original source text, and reports which documents would change. No mutations applied.
/// </summary>
public sealed class FormatVerifyService : IFormatVerifyService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<FormatVerifyService> _logger;

    public FormatVerifyService(IWorkspaceManager workspace, ILogger<FormatVerifyService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<FormatCheckResultDto> CheckAsync(
        string workspaceId, string? projectName, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.FilterProjects(solution, projectName);

        var violations = new List<FormatViolationDto>();
        var checkedCount = 0;

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                if (document.FilePath is null) continue;
                if (PathFilter.IsGeneratedOrContentFile(document.FilePath)) continue;

                checkedCount++;

                try
                {
                    var originalText = await document.GetTextAsync(ct).ConfigureAwait(false);
                    var formattedDoc = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
                    var formattedText = await formattedDoc.GetTextAsync(ct).ConfigureAwait(false);

                    // Comparing ContentEquals short-circuits on reference equality when nothing
                    // changed, then falls back to SourceText comparison which is cheap for equal
                    // content.
                    if (originalText.ContentEquals(formattedText)) continue;

                    var changes = formattedText.GetChangeRanges(originalText);
                    violations.Add(new FormatViolationDto(document.FilePath, changes.Count));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A per-document failure shouldn't block the whole pass; log and continue.
                    _logger.LogWarning(ex,
                        "FormatVerifyService: formatter threw for '{FilePath}', skipping.",
                        document.FilePath);
                }
            }
        }

        sw.Stop();
        return new FormatCheckResultDto(
            CheckedDocuments: checkedCount,
            ViolationCount: violations.Count,
            Violations: violations,
            ElapsedMs: sw.ElapsedMilliseconds);
    }
}
