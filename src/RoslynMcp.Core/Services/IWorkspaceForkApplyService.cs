using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Replays a stored preview into an isolated workspace fork and validates the fork.
/// </summary>
public interface IWorkspaceForkApplyService
{
    Task<WorkspaceForkApplyResultDto> ApplyAsync(
        string workspaceId,
        string previewToken,
        string retention,
        bool runTests,
        string? testFilter,
        string? forkName,
        CancellationToken ct);
}
