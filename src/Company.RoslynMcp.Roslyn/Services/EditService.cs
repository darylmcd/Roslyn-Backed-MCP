using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class EditService : IEditService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<EditService> _logger;

    public EditService(IWorkspaceManager workspace, ILogger<EditService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var normalizedPath = Path.GetFullPath(filePath);

        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null &&
                Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (document is null)
            throw new KeyNotFoundException($"Document not found in workspace: {filePath}");

        var sourceText = await document.GetTextAsync(ct).ConfigureAwait(false);
        var originalText = sourceText.ToString();

        // Sort edits in reverse order to apply from bottom to top (so offsets remain valid)
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();

        var textChanges = new List<TextChange>();
        foreach (var edit in sortedEdits)
        {
            var startPosition = sourceText.Lines.GetPosition(new LinePosition(edit.StartLine - 1, edit.StartColumn - 1));
            var endPosition = sourceText.Lines.GetPosition(new LinePosition(edit.EndLine - 1, edit.EndColumn - 1));
            var span = TextSpan.FromBounds(startPosition, endPosition);
            textChanges.Add(new TextChange(span, edit.NewText));
        }

        var newSourceText = sourceText.WithChanges(textChanges);
        var newDocument = document.WithText(newSourceText);
        var newSolution = newDocument.Project.Solution;

        var applied = _workspace.TryApplyChanges(workspaceId, newSolution);
        if (!applied)
        {
            return new TextEditResultDto(false, filePath, 0, []);
        }

        // Compute diff
        var newText = newSourceText.ToString();
        var differ = new Differ();
        var diffResult = InlineDiffBuilder.Diff(originalText, newText);

        var diffLines = new List<string>();
        foreach (var line in diffResult.Lines)
        {
            var prefix = line.Type switch
            {
                ChangeType.Inserted => "+ ",
                ChangeType.Deleted => "- ",
                _ => "  "
            };
            diffLines.Add(prefix + line.Text);
        }

        var fileChange = new FileChangeDto(filePath, string.Join('\n', diffLines));

        return new TextEditResultDto(true, filePath, edits.Count, [fileChange]);
    }
}
