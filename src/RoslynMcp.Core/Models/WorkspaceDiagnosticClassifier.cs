namespace RoslynMcp.Core.Models;

/// <summary>
/// Canonical classification for workspace diagnostics that require a build before
/// analyzer-backed operations can be considered ready.
/// </summary>
public static class WorkspaceDiagnosticClassifier
{
    private const string UnresolvedAnalyzerDiagnosticId = "WORKSPACE_UNRESOLVED_ANALYZER";

    public static bool IsBuildRequired(DiagnosticDto diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return string.Equals(
            diagnostic.Id,
            UnresolvedAnalyzerDiagnosticId,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasBuildRequired(IEnumerable<DiagnosticDto> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Any(IsBuildRequired);
    }
}
