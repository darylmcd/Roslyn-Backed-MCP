using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Classifies <see cref="WorkspaceDiagnostic"/> entries into a normalized severity string
/// at the point of ingress (the workspace failure handler) so every reader — `workspace_load`,
/// `workspace_status`, `roslyn://workspaces`, and `project_diagnostics` — sees the same
/// severity for the same diagnostic. Previously the normalization was applied only inside
/// `DiagnosticService.GetDiagnosticsAsync`, so other readers leaked the raw `Error` severity
/// for informational SDK messages like "PackageReference X will not be pruned".
/// </summary>
public static class WorkspaceDiagnosticSeverityClassifier
{
    /// <summary>
    /// Returns the canonical severity string ("Error", "Warning") for a Roslyn workspace
    /// diagnostic. Failures are normally `Error`, but a small allowlist of known
    /// informational SDK messages is downgraded to `Warning`.
    /// </summary>
    public static string Classify(WorkspaceDiagnosticKind kind, string message)
    {
        var raw = kind == WorkspaceDiagnosticKind.Failure ? "Error" : "Warning";
        if (raw == "Error" && IsInformationalWorkspaceFailureMessage(message))
        {
            return "Warning";
        }

        return raw;
    }

    /// <summary>
    /// Returns true if the message matches a known informational SDK pattern that
    /// MSBuildWorkspace surfaces as a failure but should not gate-fail callers.
    /// </summary>
    public static bool IsInformationalWorkspaceFailureMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        // SDK package pruning hints (e.g. "PackageReference X will not be pruned...")
        if (message.Contains("pruned", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
