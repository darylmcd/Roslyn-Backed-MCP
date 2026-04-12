using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IChangeTracker
{
    void RecordChange(string workspaceId, string description,
        IReadOnlyList<string> affectedFiles, string toolName);

    IReadOnlyList<WorkspaceChangeDto> GetChanges(string workspaceId);

    void Clear(string workspaceId);
}
