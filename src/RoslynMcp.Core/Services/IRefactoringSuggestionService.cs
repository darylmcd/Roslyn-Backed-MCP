using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IRefactoringSuggestionService
{
    Task<IReadOnlyList<RefactoringSuggestionDto>> SuggestRefactoringsAsync(
        string workspaceId, string? projectFilter, int limit, CancellationToken ct);
}
