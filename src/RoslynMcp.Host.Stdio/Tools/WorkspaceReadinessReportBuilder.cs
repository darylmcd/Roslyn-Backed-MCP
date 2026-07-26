using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Pure, side-effect-free assembly of the <see cref="WorkspaceReadinessReportDto"/> surface for the
/// <c>workspace_readiness_report</c> tool. Extracted from <see cref="WorkspaceTools"/> so the tool
/// method stays a thin read-gate dispatch wrapper. All members are stateless; the entire fan-in is
/// <see cref="WorkspaceTools.GetWorkspaceReadinessReport"/>.
/// </summary>
internal static class WorkspaceReadinessReportBuilder
{
    private const string ReadinessVerdictReady = "ready";
    private const string ReadinessVerdictRestoreNeeded = "restore-needed";
    private const string ReadinessVerdictBuildNeeded = "build-needed";
    private const string ReadinessVerdictAnalyzerLimited = "analyzer-limited";

    internal static WorkspaceReadinessReportDto Create(
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        WorkspaceStatusDto status,
        WorkspaceStatusSummaryDto summary,
        int? sourceGeneratedDocumentCount,
        string? sourceGeneratedProbeLimitation)
    {
        var buildRequired = summary.BuildRequired
            || WorkspaceDiagnosticClassifier.HasBuildRequired(status.WorkspaceDiagnostics);
        var verdict = ResolveReadinessVerdict(summary, buildRequired);
        var limitations = BuildReadinessLimitations(summary, sourceGeneratedProbeLimitation);
        var target = new WorkspaceReadinessTargetDto(
            WorkspaceId: summary.WorkspaceId,
            LoadedPath: summary.LoadedPath,
            Verdict: verdict,
            ReadinessSummary: summary,
            ProjectCount: status.ProjectCount,
            DocumentCount: status.DocumentCount,
            TestProjectCount: status.Projects.Count(project => project.IsTestProject),
            SourceGeneratedDocumentCount: sourceGeneratedDocumentCount,
            Signals: BuildReadinessSignals(summary, buildRequired));

        return new WorkspaceReadinessReportDto(
            ReportKind: "first-run-readiness",
            Status: "reported",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: target,
            Limitations: limitations,
            NextRecommendedWorkflows: BuildReadinessWorkflows(verdict, summary));
    }

    internal static WorkspaceReadinessReportDto CreateWithoutTarget(
        string status,
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces)
    {
        WorkspaceRecommendedWorkflowDto[] workflows = status switch
        {
            "no-workspace-loaded" =>
            [
                new("workspace_load", "Load a .sln, .slnx, or .csproj path before requesting a readiness report.")
            ],
            "workspace-id-required" =>
            [
                new("workspace_readiness_report", "Pass workspaceId because multiple workspaces are loaded.")
            ],
            "workspace-not-found" =>
            [
                new("workspace_list", "Pick a current workspaceId, or call workspace_load again if the host restarted or the workspace was closed or evicted.")
            ],
            _ =>
            [
                new("workspace_readiness_report", "Retry with a valid workspaceId.")
            ]
        };

        return new WorkspaceReadinessReportDto(
            ReportKind: "first-run-readiness",
            Status: status,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: null,
            Limitations:
            [
                "No tests, dotnet build, or dotnet restore were run by this report."
            ],
            NextRecommendedWorkflows: workflows);
    }

    private static string ResolveReadinessVerdict(
        WorkspaceStatusSummaryDto summary,
        bool buildRequired)
    {
        if (buildRequired)
        {
            return ReadinessVerdictBuildNeeded;
        }

        if (summary.RestoreRequired)
        {
            return ReadinessVerdictRestoreNeeded;
        }

        if (!summary.AnalyzersReady)
        {
            return ReadinessVerdictAnalyzerLimited;
        }

        if (summary.IsReady)
        {
            return ReadinessVerdictReady;
        }

        return summary.WorkspaceErrorCount > 0 || summary.IsStale
            ? ReadinessVerdictBuildNeeded
            : ReadinessVerdictAnalyzerLimited;
    }

    private static IReadOnlyList<string> BuildReadinessLimitations(
        WorkspaceStatusSummaryDto summary,
        string? sourceGeneratedProbeLimitation)
    {
        var limitations = new List<string>
        {
            "No tests, dotnet build, or dotnet restore were run by this report.",
            "The report reflects the current MSBuildWorkspace snapshot; run workspace_reload after external file edits.",
            "source_generated_documents is capped by the host setting ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS."
        };

        if (!summary.AnalyzersReady)
        {
            limitations.Add("Analyzer-driven tools may under-report until analyzer warnings are resolved.");
        }

        if (!string.IsNullOrWhiteSpace(sourceGeneratedProbeLimitation))
        {
            limitations.Add(sourceGeneratedProbeLimitation);
        }

        return limitations.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> BuildReadinessSignals(
        WorkspaceStatusSummaryDto summary,
        bool buildRequired)
    {
        var signals = new List<string>
        {
            $"projects={summary.ProjectCount}",
            $"documents={summary.DocumentCount}",
            $"workspaceDiagnostics={summary.WorkspaceDiagnosticCount}",
            $"workspaceErrors={summary.WorkspaceErrorCount}",
            $"workspaceWarnings={summary.WorkspaceWarningCount}",
            $"restoreRequired={summary.RestoreRequired.ToString().ToLowerInvariant()}",
            $"buildRequired={buildRequired.ToString().ToLowerInvariant()}",
            $"analyzersReady={summary.AnalyzersReady.ToString().ToLowerInvariant()}",
            $"isStale={summary.IsStale.ToString().ToLowerInvariant()}"
        };

        if (!string.IsNullOrWhiteSpace(summary.RestoreHint))
        {
            signals.Add($"hint={summary.RestoreHint}");
        }

        return signals;
    }

    private static IReadOnlyList<WorkspaceRecommendedWorkflowDto> BuildReadinessWorkflows(
        string verdict,
        WorkspaceStatusSummaryDto summary)
    {
        return verdict switch
        {
            ReadinessVerdictReady =>
            [
                new("recommend_workflow", "Choose the next task-specific workflow now that the workspace is ready."),
                new("symbol_search", "Start semantic navigation against the loaded solution."),
                new("compile_check", "Run a read-side compile check when you need build diagnostics; this report did not run one."),
                new("test_discover", "Inspect test discoverability when onboarding needs test entry points; this report did not run tests.")
            ],
            ReadinessVerdictRestoreNeeded =>
            [
                new("dotnet restore + workspace_reload", "Restore package assets for the loaded solution or project, then refresh the workspace snapshot."),
                new("workspace_load(autoRestore=true)", "Use this on the next load if the client is allowed to run dotnet restore automatically.")
            ],
            ReadinessVerdictBuildNeeded =>
            [
                new("dotnet build + workspace_reload", "Build missing analyzer or project outputs, then refresh the workspace snapshot."),
                new("compile_check", "After reload, inspect remaining compiler diagnostics without running tests.")
            ],
            ReadinessVerdictAnalyzerLimited =>
            [
                new("workspace_status(verbose=true)", "Inspect workspace diagnostics and project metadata before trusting analyzer-driven tools."),
                new("project_diagnostics", "Use diagnostics with analyzer limitations in mind until analyzer readiness is resolved.")
            ],
            _ =>
            [
                new("workspace_status", "Re-check the workspace status before continuing.")
            ]
        };
    }

}
