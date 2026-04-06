using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompletionService : ICompletionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CompletionService> _logger;

    public CompletionService(IWorkspaceManager workspace, ILogger<CompletionService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<CompletionResultDto> GetCompletionsAsync(
        string workspaceId,
        string filePath,
        int line,
        int column,
        string? filterText,
        int maxItems,
        CancellationToken ct)
    {
        if (maxItems <= 0)
            throw new ArgumentException("maxItems must be greater than 0.", nameof(maxItems));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
        {
            return new CompletionResultDto([], false);
        }

        var completionService = Microsoft.CodeAnalysis.Completion.CompletionService.GetService(document);
        if (completionService is null)
        {
            return new CompletionResultDto([], false);
        }

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (line < 1 || line > text.Lines.Count)
        {
            throw new ArgumentException(
                $"Line {line} is out of range. The file has {text.Lines.Count} line(s).",
                nameof(line));
        }

        var position = text.Lines[line - 1].Start + (column - 1);

        var completions = await completionService.GetCompletionsAsync(document, position, cancellationToken: ct).ConfigureAwait(false);
        if (completions is null)
        {
            return new CompletionResultDto([], false);
        }

        // UX-007: Apply the prefix filter BEFORE pagination so the limit refers to filtered
        // results rather than the raw Roslyn output. The filter matches against the FilterText
        // when present (the canonical Roslyn filter source) and falls back to DisplayText.
        IEnumerable<Microsoft.CodeAnalysis.Completion.CompletionItem> source = completions.ItemsList;
        if (!string.IsNullOrEmpty(filterText))
        {
            source = source.Where(item =>
            {
                var candidate = !string.IsNullOrEmpty(item.FilterText) ? item.FilterText : item.DisplayText;
                return candidate.StartsWith(filterText, StringComparison.OrdinalIgnoreCase);
            });
        }

        var filteredList = source.ToList();
        var pagedItems = filteredList.Take(maxItems).ToList();

        var items = pagedItems
            .Select(item => new CompletionItemDto(
                DisplayText: item.DisplayText,
                FilterText: item.FilterText,
                SortText: item.SortText,
                InlineDescription: BuildCompletionInlineDescription(item),
                Kind: item.Tags.Length > 0 ? item.Tags[0] : "Unknown",
                Tags: item.Tags.Length > 0 ? item.Tags.ToList() : null))
            .ToList();

        return new CompletionResultDto(items, filteredList.Count > items.Count);
    }

    private static string? BuildCompletionInlineDescription(CompletionItem item)
    {
        if (!string.IsNullOrEmpty(item.InlineDescription))
            return item.InlineDescription;

        return item.Properties.TryGetValue("Description", out var desc) ? desc : null;
    }
}
