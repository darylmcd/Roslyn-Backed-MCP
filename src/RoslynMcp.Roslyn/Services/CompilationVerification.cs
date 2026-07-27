using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Captures complete error snapshots and computes introduced diagnostics for every
/// apply-and-verify workflow. Keeping pagination and identity-diff policy here prevents
/// individual callers from silently truncating a correctness decision.
/// </summary>
internal static class CompilationVerification
{
    public static async Task<CompilationErrorSnapshot> CaptureAsync(
        ICompileCheckService compileCheckService,
        string workspaceId,
        string? projectFilter,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(compileCheckService);

        var check = await compileCheckService.CheckAsync(
            workspaceId,
            new CompileCheckOptions(
                ProjectFilter: projectFilter,
                SeverityFilter: "error",
                Limit: int.MaxValue),
            ct).ConfigureAwait(false);

        return new CompilationErrorSnapshot(
            check,
            DiagnosticIdentitySet.ExtractErrorIdentities(check));
    }

    public static IReadOnlyList<DiagnosticDto> FindIntroducedDiagnostics(
        CompilationErrorSnapshot baseline,
        CompilationErrorSnapshot current)
    {
        var introducedIdentities = new HashSet<string>(
            current.ErrorIdentities.Except(baseline.ErrorIdentities),
            StringComparer.Ordinal);

        return current.Check.Diagnostics
            .Where(diagnostic =>
                string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase)
                && introducedIdentities.Contains(DiagnosticIdentitySet.FormatIdentity(diagnostic)))
            .ToList();
    }
}

internal sealed record CompilationErrorSnapshot(
    CompileCheckDto Check,
    HashSet<string> ErrorIdentities)
{
    public bool Cancelled => Check.Cancelled;
    public int ErrorCount => Check.ErrorCount;
}
