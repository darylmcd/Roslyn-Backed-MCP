using RoslynMcp.Core.Services;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of replaying and validating a preview in a server-owned workspace fork.
/// </summary>
public sealed record WorkspaceForkApplyResultDto(
    bool Success,
    string? ForkWorkspaceId,
    string ForkPath,
    bool Retained,
    IReadOnlyList<string> AppliedFiles,
    WorkspaceValidationDto Validation,
    TestRunResultDto? ExplicitTestRun,
    IReadOnlyList<string> CleanupWarnings);
