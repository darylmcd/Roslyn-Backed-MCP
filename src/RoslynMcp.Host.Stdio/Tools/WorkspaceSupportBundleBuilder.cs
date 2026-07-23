using System.Reflection;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Pure, side-effect-free assembly of the <see cref="WorkspaceSupportBundleDto"/> surface for the
/// <c>workspace_support_bundle</c> tool. Extracted from <see cref="WorkspaceTools"/> so the tool
/// method stays a thin read-gate dispatch wrapper. All members are stateless; the entire fan-in is
/// <see cref="WorkspaceTools.GetWorkspaceSupportBundle"/>.
/// </summary>
internal static class WorkspaceSupportBundleBuilder
{
    internal static WorkspaceSupportBundleDto Create(
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        WorkspaceStatusSummaryDto readiness,
        WorkspaceDriftResult drift,
        DiagnosticsResultDto diagnostics,
        IReadOnlyList<WorkspaceChangeDto> changes,
        int changeCap,
        int driftCap)
    {
        var returnedChanges = TakeRecentChanges(changes, changeCap);
        var returnedDriftedFiles = drift.FilesDrifted.Take(driftCap).ToArray();
        var target = new WorkspaceSupportTargetDto(
            WorkspaceId: readiness.WorkspaceId,
            LoadedPath: readiness.LoadedPath,
            ReadinessSummary: readiness,
            Drift: new WorkspaceSupportDriftDto(
                Stale: drift.Stale,
                TotalDriftedFiles: drift.FilesDrifted.Count,
                ReturnedDriftedFiles: returnedDriftedFiles.Length,
                HasMore: drift.FilesDrifted.Count > returnedDriftedFiles.Length,
                FilesDrifted: returnedDriftedFiles,
                Recommended: drift.Recommended),
            Version: new WorkspaceSupportVersionDto(
                WorkspaceVersion: readiness.WorkspaceVersion,
                SnapshotToken: readiness.SnapshotToken,
                ServerVersion: ResolveServerVersion(),
                CatalogVersion: ServerSurfaceCatalog.CatalogVersion));

        var diagnosticTotals = new WorkspaceSupportDiagnosticsTotalsDto(
            TotalErrors: diagnostics.TotalErrors,
            TotalWarnings: diagnostics.TotalWarnings,
            TotalInfo: diagnostics.TotalInfo,
            CompilerErrors: diagnostics.CompilerErrors,
            AnalyzerErrors: diagnostics.AnalyzerErrors,
            WorkspaceErrors: diagnostics.WorkspaceErrors);

        var ledger = new WorkspaceSupportChangeLedgerDto(
            TotalChanges: changes.Count,
            ReturnedChanges: returnedChanges.Count,
            HasMore: changes.Count > returnedChanges.Count,
            Changes: returnedChanges);

        var nextActions = BuildSupportBundleNextActions(
            readiness,
            drift,
            diagnosticTotals,
            ledger);

        return new WorkspaceSupportBundleDto(
            BundleKind: "incident-support",
            Status: ResolveSupportBundleStatus(readiness, drift, diagnosticTotals, ledger),
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: target,
            DiagnosticsTotals: diagnosticTotals,
            ChangeLedger: ledger,
            Limits: new WorkspaceSupportLimitsDto(changeCap, driftCap, SourceSnippetsIncluded: false),
            NextActions: nextActions);
    }

    internal static WorkspaceSupportBundleDto CreateWithoutTarget(
        string status,
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        int changeCap,
        int driftCap)
    {
        string[] nextActions = status switch
        {
            "no-workspace-loaded" =>
            [
                "Call workspace_load with a .sln, .slnx, or .csproj path before requesting a workspace support bundle."
            ],
            "workspace-id-required" =>
            [
                "Pass the workspaceId to workspace_support_bundle because multiple workspaces are loaded."
            ],
            "workspace-not-found" =>
            [
                "Call workspace_list to pick a current workspaceId, or call workspace_load again if the host restarted or the workspace was closed or evicted."
            ],
            _ => ["Retry workspace_support_bundle with a valid workspaceId."]
        };

        return new WorkspaceSupportBundleDto(
            BundleKind: "incident-support",
            Status: status,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: null,
            DiagnosticsTotals: null,
            ChangeLedger: new WorkspaceSupportChangeLedgerDto(0, 0, HasMore: false, Changes: []),
            Limits: new WorkspaceSupportLimitsDto(changeCap, driftCap, SourceSnippetsIncluded: false),
            NextActions: nextActions);
    }

    private static IReadOnlyList<WorkspaceChangeDto> TakeRecentChanges(
        IReadOnlyList<WorkspaceChangeDto> changes,
        int cap)
    {
        if (cap == 0 || changes.Count == 0)
        {
            return [];
        }

        var skip = Math.Max(0, changes.Count - cap);
        return changes.Skip(skip).ToArray();
    }

    private static IReadOnlyList<string> BuildSupportBundleNextActions(
        WorkspaceStatusSummaryDto readiness,
        WorkspaceDriftResult drift,
        WorkspaceSupportDiagnosticsTotalsDto diagnostics,
        WorkspaceSupportChangeLedgerDto ledger)
    {
        var actions = new List<string>();
        if (drift.Stale || readiness.IsStale)
        {
            actions.Add($"Run workspace_reload for {readiness.WorkspaceId}; the workspace snapshot is stale or drifted from disk.");
        }

        if (readiness.RestoreRequired)
        {
            actions.Add("Run dotnet restore on the loaded solution or project, then workspace_reload.");
        }

        if (!readiness.AnalyzersReady)
        {
            actions.Add("Resolve analyzer-load warnings before trusting analyzer-driven tools such as project_diagnostics or find_unused_symbols.");
        }

        if (!string.IsNullOrWhiteSpace(readiness.RestoreHint))
        {
            actions.Add(readiness.RestoreHint);
        }

        if (diagnostics.TotalErrors > 0)
        {
            actions.Add("Run compile_check or project_diagnostics to inspect the error diagnostics.");
        }
        else if (diagnostics.TotalWarnings > 0)
        {
            actions.Add("Run project_diagnostics with filters if warning details are needed.");
        }
        else if (diagnostics.TotalInfo > 0)
        {
            actions.Add("Run project_diagnostics with severity=\"Info\" if informational diagnostic details are needed.");
        }

        if (ledger.TotalChanges > 0)
        {
            actions.Add("Run validate_recent_git_changes before handing off or continuing after session mutations.");
        }

        if (actions.Count == 0)
        {
            actions.Add("Workspace support bundle is clean; proceed with read-side tools or the intended workflow.");
        }

        return actions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ResolveSupportBundleStatus(
        WorkspaceStatusSummaryDto readiness,
        WorkspaceDriftResult drift,
        WorkspaceSupportDiagnosticsTotalsDto diagnostics,
        WorkspaceSupportChangeLedgerDto ledger)
    {
        if (drift.Stale || readiness.IsStale)
        {
            return "stale";
        }

        if (!readiness.IsReady || diagnostics.TotalErrors > 0 || diagnostics.TotalWarnings > 0 || diagnostics.TotalInfo > 0)
        {
            return "attention-needed";
        }

        return ledger.TotalChanges > 0 ? "changed" : "clean";
    }

    private static string ResolveServerVersion()
    {
        var assembly = typeof(WorkspaceSupportBundleBuilder).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }
}
