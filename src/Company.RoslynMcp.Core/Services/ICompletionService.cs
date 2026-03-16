using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface ICompletionService
{
    Task<CompletionResultDto> GetCompletionsAsync(
        string workspaceId, string filePath, int line, int column, CancellationToken ct);
}
