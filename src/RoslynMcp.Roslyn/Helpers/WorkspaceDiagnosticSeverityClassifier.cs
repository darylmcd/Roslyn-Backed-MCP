using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

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

        if (IsKnownSdkWarning(message))
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
    /// Recognizes actual SDK warnings that MSBuildWorkspace reports as Failure. Unlike
    /// presentation-only VS toolchain downgrades, these do not indicate a failed native load.
    /// Match complete diagnostic bodies so project paths and task-error fragments cannot
    /// turn a real failure into a warning.
    /// </summary>
    internal static bool IsKnownSdkWarning(string message)
    {
        if (string.IsNullOrEmpty(message) || message.Contains('\r') || message.Contains('\n'))
            return false;

        const string wrapper = "Msbuild failed when processing the file '";
        const string delimiter = "' with message: ";
        if (message.StartsWith(wrapper, StringComparison.Ordinal))
        {
            var separator = message.LastIndexOf(delimiter, StringComparison.Ordinal);
            if (separator < wrapper.Length) return false;
            message = message[(separator + delimiter.Length)..];
        }

        // Microsoft.Build.Tasks.Git.LocateRepository logs this as a warning when a
        // detached source copy has no repository; Git read/task failures remain errors.
        const string missingRepository = "Unable to locate repository with working directory that contains directory '";
        if (message.StartsWith(missingRepository, StringComparison.Ordinal)
            && message.EndsWith("'.", StringComparison.Ordinal)
            && message.Length > missingRepository.Length + 2)
            return true;

        // NU1510: exact NuGet.Commands Error_RestorePruningDirectPackageReference body.
        const string pruningPrefix = "PackageReference ";
        const string pruningSuffix = " will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.";
        if (message.StartsWith("NU1510: ", StringComparison.Ordinal)) message = message[8..];
        if (!message.StartsWith(pruningPrefix, StringComparison.Ordinal)
            || !message.EndsWith(pruningSuffix, StringComparison.Ordinal))
            return false;
        var packageLength = message.Length - pruningPrefix.Length - pruningSuffix.Length;
        if (packageLength <= 0) return false;
        var package = message.AsSpan(pruningPrefix.Length, packageLength);
        return !package.ContainsAny(' ', '\t');
    }

    /// <summary>Delegates VS-MSBuild detection to the shared Core policy.</summary>
    public static bool IsVsMsbuildRequiredMessage(string message) =>
        WorkspaceToolchainClassifier.IsVsMsbuildRequiredMessage(message);
}
