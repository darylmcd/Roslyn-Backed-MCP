namespace RoslynMcp.Core.Models;

/// <summary>
/// First-run workspace readiness report for onboarding flows. This is intentionally separate
/// from <c>workspace_support_bundle</c>: it avoids incident-only drift/change-ledger details and
/// reports whether a newly loaded workspace is ready for normal Roslyn MCP workflows.
/// </summary>
public sealed record WorkspaceReadinessReportDto(
    string ReportKind,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    string? RequestedWorkspaceId,
    IReadOnlyList<WorkspaceStatusSummaryDto> LoadedWorkspaces,
    WorkspaceReadinessTargetDto? Workspace,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<WorkspaceRecommendedWorkflowDto> NextRecommendedWorkflows);

public sealed record WorkspaceReadinessTargetDto(
    string WorkspaceId,
    string? LoadedPath,
    string Verdict,
    WorkspaceStatusSummaryDto ReadinessSummary,
    int ProjectCount,
    int DocumentCount,
    int TestProjectCount,
    int? SourceGeneratedDocumentCount,
    IReadOnlyList<string> Signals);

public sealed record WorkspaceRecommendedWorkflowDto(
    string Workflow,
    string Reason);
