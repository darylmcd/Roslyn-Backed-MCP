using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

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
        string workspaceId, string filePath, int line, int column, CancellationToken ct)
    {
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
        var position = text.Lines[line - 1].Start + (column - 1);

        var completions = await completionService.GetCompletionsAsync(document, position, cancellationToken: ct).ConfigureAwait(false);
        if (completions is null)
        {
            return new CompletionResultDto([], false);
        }

        var items = completions.ItemsList
            .Take(100)
            .Select(item => new CompletionItemDto(
                DisplayText: item.DisplayText,
                FilterText: item.FilterText,
                SortText: item.SortText,
                InlineDescription: item.InlineDescription,
                Kind: item.Tags.Length > 0 ? item.Tags[0] : "Unknown",
                Tags: item.Tags.Length > 0 ? item.Tags.ToList() : null))
            .ToList();

        return new CompletionResultDto(items, completions.ItemsList.Count > 100);
    }
}
