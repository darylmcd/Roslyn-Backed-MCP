using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface ISyntaxService
{
    Task<SyntaxNodeDto?> GetSyntaxTreeAsync(string workspaceId, string filePath, int? startLine, int? endLine, int maxDepth, CancellationToken ct);
}
