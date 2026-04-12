namespace RoslynMcp.Core.Models;

public sealed record WorkspaceChangeDto(
    int SequenceNumber,
    string Description,
    IReadOnlyList<string> AffectedFiles,
    string ToolName,
    DateTime AppliedAtUtc);
