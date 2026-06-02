namespace RoslynMcp.Core.Models;

/// <summary>
/// Incident-diagnosis bundle for an already-running workspace session. This is intentionally
/// separate from first-run readiness reporting: it composes current readiness, drift, session
/// ledger, surface version, and diagnostic totals so an operator can recover a stuck/stale
/// session without pulling source snippets into the model context.
/// </summary>
public sealed record WorkspaceSupportBundleDto(
    string BundleKind,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    string? RequestedWorkspaceId,
    IReadOnlyList<WorkspaceStatusSummaryDto> LoadedWorkspaces,
    WorkspaceSupportTargetDto? Workspace,
    WorkspaceSupportDiagnosticsTotalsDto? DiagnosticsTotals,
    WorkspaceSupportChangeLedgerDto ChangeLedger,
    WorkspaceSupportLimitsDto Limits,
    IReadOnlyList<string> NextActions);

public sealed record WorkspaceSupportTargetDto(
    string WorkspaceId,
    string? LoadedPath,
    WorkspaceStatusSummaryDto ReadinessSummary,
    WorkspaceSupportDriftDto Drift,
    WorkspaceSupportVersionDto Version);

public sealed record WorkspaceSupportDriftDto(
    bool Stale,
    int TotalDriftedFiles,
    int ReturnedDriftedFiles,
    bool HasMore,
    IReadOnlyList<string> FilesDrifted,
    string Recommended);

public sealed record WorkspaceSupportVersionDto(
    int WorkspaceVersion,
    string SnapshotToken,
    string ServerVersion,
    string CatalogVersion);

public sealed record WorkspaceSupportDiagnosticsTotalsDto(
    int TotalErrors,
    int TotalWarnings,
    int TotalInfo,
    int CompilerErrors,
    int AnalyzerErrors,
    int WorkspaceErrors);

public sealed record WorkspaceSupportChangeLedgerDto(
    int TotalChanges,
    int ReturnedChanges,
    bool HasMore,
    IReadOnlyList<WorkspaceChangeDto> Changes);

public sealed record WorkspaceSupportLimitsDto(
    int MaxChangeEntries,
    int MaxDriftedFiles,
    bool SourceSnippetsIncluded);
