namespace RoslynMcp.Core.Services;

/// <summary>Recognizes workspace diagnostics that require Visual Studio MSBuild.</summary>
public static class WorkspaceToolchainClassifier
{
    /// <summary>Returns whether a message identifies a COM or .NET Core MSBuild limitation.</summary>
    public static bool IsVsMsbuildRequiredMessage(string? message) =>
        !string.IsNullOrEmpty(message)
        && (message.Contains("ResolveComReference", StringComparison.OrdinalIgnoreCase)
            || message.Contains("type library", StringComparison.OrdinalIgnoreCase)
            || message.Contains("The .NET Core version of MSBuild", StringComparison.OrdinalIgnoreCase));
}
