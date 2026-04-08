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

        // BUG fix (get-completions-ranking): boost in-scope candidates so locals/parameters
        // beat type members, type members beat types, and types beat the long tail (namespaces,
        // external symbols). Roslyn's CompletionItem.Tags carry the high-level kind which is
        // enough to bucket without resolving the underlying ISymbol. Stable secondary sort
        // by SortText preserves Roslyn's intra-bucket ordering.
        var filteredList = source
            .OrderBy(InScopeRank)
            .ThenBy(item => item.SortText, StringComparer.Ordinal)
            .ToList();
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

    /// <summary>
    /// Buckets a completion candidate by how "in-scope" it is. Lower values rank first.
    /// 0 = locals/parameters, 1 = type members (methods/properties/fields/events),
    /// 2 = types (classes/structs/interfaces/enums/delegates), 3 = everything else
    /// (namespaces, modules, the long tail of external symbols).
    /// </summary>
    private static int InScopeRank(CompletionItem item)
    {
        if (item.Tags.IsDefaultOrEmpty)
        {
            return 3;
        }

        if (item.Tags.Contains("Local") || item.Tags.Contains("Parameter") || item.Tags.Contains("RangeVariable"))
        {
            return 0;
        }

        if (item.Tags.Contains("Method") || item.Tags.Contains("Property") || item.Tags.Contains("Field")
            || item.Tags.Contains("Event") || item.Tags.Contains("ExtensionMethod"))
        {
            return 1;
        }

        if (item.Tags.Contains("Class") || item.Tags.Contains("Structure") || item.Tags.Contains("Interface")
            || item.Tags.Contains("Enum") || item.Tags.Contains("Delegate") || item.Tags.Contains("EnumMember"))
        {
            return 2;
        }

        return 3;
    }
}
