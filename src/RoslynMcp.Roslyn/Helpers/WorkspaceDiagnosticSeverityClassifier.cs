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

        // VS-MSBuild-only failure indicators: COM references and .NET Framework-only MSBuild
        // tasks that the .NET Core MSBuild bundled with this server cannot resolve. Downgrade
        // to Warning so partial-load UX surfaces a concrete remediation hint instead of
        // gating callers with raw WORKSPACE_FAILURE errors that suggest `dotnet restore`.
        // See `workspace-toolchain-status-preflight` (BioRemote 2026-05).
        if (IsVsMsbuildRequiredMessage(message))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the message indicates a VS-MSBuild-only feature that the .NET Core
    /// MSBuild cannot resolve. Currently matches COM-reference resolution failures
    /// (<c>ResolveComReference</c>, <c>type library</c>) and explicit .NET Core MSBuild
    /// limitation messages (<c>"The .NET Core version of MSBuild..."</c>). Public so the
    /// hint builder in <see cref="Core.Models.WorkspaceStatusSummaryDto"/> can re-use the
    /// detection without duplicating the substring list.
    /// </summary>
    public static bool IsVsMsbuildRequiredMessage(string message)
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
