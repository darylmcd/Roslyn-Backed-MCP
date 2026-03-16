using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IEditService
{
    Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct);
}
