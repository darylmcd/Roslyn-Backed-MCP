using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Configuration;

/// <summary>
/// Binds <see cref="ValidationServiceOptions"/> from the host's environment without coupling the
/// parsing to top-level startup code. The value-reader seam keeps the binding directly testable.
/// A missing, non-numeric, or non-positive value keeps the in-source default.
/// </summary>
internal static class ValidationServiceOptionsEnvironmentBinder
{
    internal const string BuildTimeoutVariable = "ROSLYNMCP_BUILD_TIMEOUT_SECONDS";
    internal const string RestoreTimeoutVariable = "ROSLYNMCP_RESTORE_TIMEOUT_SECONDS";
    internal const string TestTimeoutVariable = "ROSLYNMCP_TEST_TIMEOUT_SECONDS";
    internal const string MaxRelatedFilesVariable = "ROSLYNMCP_MAX_RELATED_FILES";
    internal const string VulnerabilityScanTimeoutVariable = "ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS";
    internal const string ApplyRevertTimeoutVariable = "ROSLYNMCP_APPLY_REVERT_TIMEOUT_SECONDS";
    internal const string GitStatusTimeoutVariable = "ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS";

    internal static ValidationServiceOptions Bind(Func<string, string?> readValue)
    {
        ArgumentNullException.ThrowIfNull(readValue);
        var opts = new ValidationServiceOptions();

        if (TryReadPositive(readValue, BuildTimeoutVariable, out var bs))
            opts = opts with { BuildTimeout = TimeSpan.FromSeconds(bs) };
        if (TryReadPositive(readValue, RestoreTimeoutVariable, out var rts))
            opts = opts with { RestoreTimeout = TimeSpan.FromSeconds(rts) };
        if (TryReadPositive(readValue, TestTimeoutVariable, out var ts))
            opts = opts with { TestTimeout = TimeSpan.FromSeconds(ts) };
        if (TryReadPositive(readValue, MaxRelatedFilesVariable, out var mrf))
            opts = opts with { MaxRelatedFiles = mrf };
        if (TryReadPositive(readValue, VulnerabilityScanTimeoutVariable, out var vs))
            opts = opts with { VulnerabilityScanTimeout = TimeSpan.FromSeconds(vs) };
        if (TryReadPositive(readValue, ApplyRevertTimeoutVariable, out var rs))
            opts = opts with { ApplyRevertTimeout = TimeSpan.FromSeconds(rs) };
        if (TryReadPositive(readValue, GitStatusTimeoutVariable, out var gss))
            opts = opts with { GitStatusTimeout = TimeSpan.FromSeconds(gss) };

        return opts;
    }

    private static bool TryReadPositive(Func<string, string?> readValue, string name, out int value) =>
        int.TryParse(readValue(name), out value) && value > 0;
}
