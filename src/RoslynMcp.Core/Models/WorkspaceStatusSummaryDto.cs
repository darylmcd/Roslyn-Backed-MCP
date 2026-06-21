namespace RoslynMcp.Core.Models;

/// <summary>
/// Lean version of <see cref="WorkspaceStatusDto"/> for budget-sensitive callers.
/// Returns counts and load state without materializing the per-project tree or
/// the full diagnostic array. Used by <c>workspace_load</c>, <c>workspace_list</c>,
/// <c>workspace_status</c>, and the <c>roslyn://workspaces</c> resource by default
/// (see <c>verbose=true</c> opt-in for the full <see cref="WorkspaceStatusDto"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Readiness model</strong> (workspace-readiness-contract-bundle, 2026-04-16):
/// <see cref="IsReady"/> is the conjunction of three conditions:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="IsLoaded"/> — workspace finished loading at least once.</description></item>
///   <item><description><see cref="IsStale"/> is false — no pending external changes.</description></item>
///   <item><description><see cref="AnalyzersReady"/> — every required analyzer reference resolved.</description></item>
///   <item><description><see cref="WorkspaceErrorCount"/> is zero.</description></item>
///   <item><description><see cref="RestoreRequired"/> is false — package-restore inputs still match the loaded assets snapshot.</description></item>
///   <item><description><see cref="BuildRequired"/> is false — no analyzer build output is missing (remedied by <c>dotnet build</c>, not <c>dotnet restore</c>).</description></item>
/// </list>
/// <para>
/// Pre-bundle the formula was just <c>IsLoaded &amp;&amp; !IsStale &amp;&amp; errors == 0</c>; analyzer-resolution
/// failures surface as <c>WORKSPACE_UNRESOLVED_ANALYZER</c> warnings (severity Warning, not Error)
/// so the old formula returned <c>IsReady=true</c> even when every analyzer-driven tool was
/// silently missing findings (Jellyfin 2026-04-16: <c>isReady=true</c> with 40 unresolved-analyzer
/// warnings). The new <see cref="AnalyzersReady"/> field exposes that condition independently.
/// </para>
/// </remarks>
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
    bool AnalyzersReady,
    bool RestoreRequired,
    bool BuildRequired,
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
        var unresolvedAnalyzerWarnings = 0;
        var vsMsbuildRequired = false;
        foreach (var diagnostic in status.WorkspaceDiagnostics)
        {
            if (string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            {
                errors++;
            }
            else if (string.Equals(diagnostic.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                warnings++;
                if (string.Equals(diagnostic.Id, "WORKSPACE_UNRESOLVED_ANALYZER", StringComparison.OrdinalIgnoreCase))
                {
                    unresolvedAnalyzerWarnings++;
                }
            }

            // VS-MSBuild-only diagnostics arrive with Id="WORKSPACE_FAILURE" and are downgraded
            // to Warning by WorkspaceDiagnosticSeverityClassifier. Detect them by message content
            // here (Core can't reference Roslyn for the helper) — mirrors the substring scan in
            // BuildRestoreHint below. Treated as an analyzers-not-ready condition so callers see
            // isReady=false and surface the VS-MSBuild remediation hint instead of proceeding
            // with degraded results.
            if (IsVsMsbuildRequiredMessage(diagnostic.Message))
            {
                vsMsbuildRequired = true;
            }
        }

        var analyzersReady = unresolvedAnalyzerWarnings == 0 && !vsMsbuildRequired;
        var solutionFileName = GetSolutionOrProjectFileName(status.LoadedPath);
        var restoreRequired = status.RestoreRequired;
        var buildRequired = status.BuildRequired;
        var isReady = status.IsLoaded && !status.IsStale && analyzersReady && errors == 0 && !restoreRequired && !buildRequired;
        var restoreHint = BuildRestoreHint(
            status.WorkspaceDiagnostics,
            isStale: status.IsStale,
            errors: errors,
            unresolvedAnalyzerWarnings: unresolvedAnalyzerWarnings,
            restoreRequired: restoreRequired,
            buildRequired: buildRequired,
            vsMsbuildRequired: vsMsbuildRequired);

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
            AnalyzersReady: analyzersReady,
            RestoreRequired: restoreRequired,
            BuildRequired: buildRequired,
            RestoreHint: restoreHint,
            SolutionFileName: solutionFileName);
    }

    /// <summary>
    /// Builds an actionable hint string explaining why the workspace is not ready.
    /// Five cases, in priority order:
    /// <list type="number">
    ///   <item><description>VS-MSBuild required (COM references, .NET Core MSBuild limitation) → tell caller this project needs Visual Studio MSBuild on PATH.</description></item>
    ///   <item><description>Build required (missing analyzer build output) → suggest <c>dotnet build</c>, NOT <c>dotnet restore</c>.</description></item>
    ///   <item><description>Restore required (missing NuGet package inputs) → suggest <c>dotnet restore</c>.</description></item>
    ///   <item><description>Unresolved analyzer warnings → tell caller analyzer-driven tools will under-report.</description></item>
    ///   <item><description>Many "could not be found" / CS0234 style errors → suggest <c>dotnet restore</c>.</description></item>
    ///   <item><description>Workspace is stale with no errors → likely transient post-apply; suggest a brief retry before reload.</description></item>
    /// </list>
    /// Returns <c>null</c> when the workspace is healthy or none of the heuristics fire.
    /// </summary>
    private static string? BuildRestoreHint(
        IReadOnlyList<DiagnosticDto> workspaceDiagnostics,
        bool isStale,
        int errors,
        int unresolvedAnalyzerWarnings,
        bool restoreRequired,
        bool buildRequired,
        bool vsMsbuildRequired)
    {
        // Highest priority: COM references / .NET Core MSBuild limitations cannot be fixed by
        // `dotnet restore` and must surface a concrete remediation BEFORE any restore-style hint.
        // See `workspace-toolchain-status-preflight` (BioRemote 2026-05).
        if (vsMsbuildRequired)
        {
            return "This project requires Visual Studio MSBuild (e.g. COM references or .NET Framework-only tasks). The .NET Core MSBuild bundled with this server cannot resolve these. Remediation: (a) remove or interop-wrap COM references, or (b) load this project from an environment where msbuild.exe (Visual Studio) is on PATH.";
        }

        // restore-required-vs-build-conflation: a missing analyzer build output is fixed by
        // `dotnet build`, not `dotnet restore`. Check it BEFORE restoreRequired so callers do not
        // get sent into a no-op restore loop.
        if (buildRequired)
        {
            return "Missing analyzer build output (e.g. an analyzer project's dll has not been produced). Run `dotnet build` on the analyzer project, then `workspace_reload`.";
        }

        if (restoreRequired)
        {
            return "Package-restore inputs changed since the last restore. Run `dotnet restore` on the solution or project, then `workspace_reload`.";
        }

        if (unresolvedAnalyzerWarnings > 0)
        {
            return $"{unresolvedAnalyzerWarnings} analyzer reference(s) failed to load — analyzer-driven tools (e.g. find_unused_symbols, project_diagnostics) will under-report. Run `dotnet restore` on the solution and reload the workspace.";
        }

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

        if (suspicious >= 3)
        {
            return "Workspace diagnostics suggest missing NuGet assets or references. Run `dotnet restore` on the solution or project, then `workspace_reload`.";
        }

        // Transient-stale heuristic: workspace is stale but has no errors. Most likely a
        // brief inconsistency window between an apply landing and the next workspace_reload
        // picking up the file change. Tools may still operate correctly during this window —
        // the bug being fixed is that callers were treating `isReady=false` as "broken" when
        // it just meant "settling." Suggest a brief retry before going to a full reload.
        if (isStale && errors == 0)
        {
            return "Workspace is stale but has no errors — likely transient (post-apply settling). Retry `workspace_status` in ~250ms; if still stale, run `workspace_reload`.";
        }

        return null;
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

    /// <summary>
    /// VS-MSBuild-only diagnostic detector. Kept local to Core (DTO layer cannot depend on
    /// the Roslyn layer for the canonical helper) — mirrors
    /// <c>RoslynMcp.Roslyn.Helpers.WorkspaceDiagnosticSeverityClassifier.IsVsMsbuildRequiredMessage</c>.
    /// Keep the two substring lists in sync.
    /// </summary>
    private static bool IsVsMsbuildRequiredMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.Contains("ResolveComReference", StringComparison.OrdinalIgnoreCase)
            || message.Contains("type library", StringComparison.OrdinalIgnoreCase)
            || message.Contains("The .NET Core version of MSBuild", StringComparison.OrdinalIgnoreCase);
    }
}
