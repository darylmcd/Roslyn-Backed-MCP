namespace RoslynMcp.Core.Models;

/// <summary>
/// Lean version of <see cref="WorkspaceStatusDto"/> for budget-sensitive callers.
/// Returns counts and load state without materializing the per-project tree or
/// the full diagnostic array. Used by <c>workspace_load</c>, <c>workspace_list</c>,
/// <c>workspace_status</c>, and the <c>roslyn://workspaces</c> resource by default
/// (see <c>verbose=true</c> opt-in for the full <see cref="WorkspaceStatusDto"/>).
/// </summary>
public sealed record WorkspaceStatusSummaryDto(
    string WorkspaceId,
    string? LoadedPath,
    int WorkspaceVersion,
    string SnapshotToken,
    DateTimeOffset LoadedAtUtc,
    int ProjectCount,
    int DocumentCount,
    bool IsLoaded,
    bool IsStale,
    int WorkspaceDiagnosticCount,
    int WorkspaceErrorCount,
    int WorkspaceWarningCount,
    bool IsReady,
    string? RestoreHint,
    string? SolutionFileName)
{
    /// <summary>
    /// Projects the verbose <see cref="WorkspaceStatusDto"/> down to its summary fields.
    /// Used at the host boundary so the underlying <c>WorkspaceManager</c> still produces
    /// the canonical verbose shape internally; only the serialized output narrows.
    /// </summary>
    public static WorkspaceStatusSummaryDto From(WorkspaceStatusDto status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var errors = 0;
        var warnings = 0;
        foreach (var diagnostic in status.WorkspaceDiagnostics)
        {
            if (string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                errors++;
            }
            else if (string.Equals(diagnostic.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                warnings++;
            }
        }

        var restoreHint = BuildRestoreHint(status.WorkspaceDiagnostics);
        var solutionFileName = GetSolutionOrProjectFileName(status.LoadedPath);
        var isReady = status.IsLoaded && !status.IsStale && errors == 0;

        return new WorkspaceStatusSummaryDto(
            WorkspaceId: status.WorkspaceId,
            LoadedPath: status.LoadedPath,
            WorkspaceVersion: status.WorkspaceVersion,
            SnapshotToken: status.SnapshotToken,
            LoadedAtUtc: status.LoadedAtUtc,
            ProjectCount: status.ProjectCount,
            DocumentCount: status.DocumentCount,
            IsLoaded: status.IsLoaded,
            IsStale: status.IsStale,
            WorkspaceDiagnosticCount: status.WorkspaceDiagnostics.Count,
            WorkspaceErrorCount: errors,
            WorkspaceWarningCount: warnings,
            IsReady: isReady,
            RestoreHint: restoreHint,
            SolutionFileName: solutionFileName);
    }

    /// <summary>
    /// Heuristic: several missing-metadata / namespace style workspace failures often mean
    /// packages were not restored before opening the workspace.
    /// </summary>
    private static string? BuildRestoreHint(IReadOnlyList<DiagnosticDto> workspaceDiagnostics)
    {
        var suspicious = 0;
        foreach (var d in workspaceDiagnostics)
        {
            if (string.Equals(d.Id, "CS0234", StringComparison.OrdinalIgnoreCase))
            {
                suspicious++;
                continue;
            }

            var msg = d.Message;
            if (msg is null) continue;
            if (msg.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("does not exist in the namespace", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("could not load file or assembly", StringComparison.OrdinalIgnoreCase))
            {
                suspicious++;
            }
        }

        return suspicious >= 3
            ? "Workspace diagnostics suggest missing NuGet assets or references. Run `dotnet restore` on the solution or project, then `workspace_reload`."
            : null;
    }

    private static string? GetSolutionOrProjectFileName(string? loadedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath)) return null;
        var file = Path.GetFileName(loadedPath);
        if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return file;
        return file;
    }
}
