using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Security;

/// <summary>
/// Binds security options from the host's environment without coupling parsing to top-level
/// startup code. The value-based seam keeps platform-delimited root parsing directly testable.
/// </summary>
internal static class SecurityOptionsEnvironmentBinder
{
    internal const string SanctionedRootsVariable = "ROSLYNMCP_SANCTIONED_ROOTS";
    internal const string PathValidationFailOpenVariable = "ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN";
    internal const string AllowRootExpansionVariable = "ROSLYNMCP_ALLOW_ROOT_EXPANSION";

    internal static SecurityOptions Bind(
        string? sanctionedRootsValue,
        string? failOpenValue,
        string? allowRootExpansionValue)
    {
        var sanctionedRoots = string.IsNullOrWhiteSpace(sanctionedRootsValue)
            ? []
            : sanctionedRootsValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new SecurityOptions
        {
            SanctionedRoots = sanctionedRoots,
            PathValidationFailOpen = bool.TryParse(failOpenValue, out var failOpen) && failOpen,
            AllowRootExpansion = bool.TryParse(allowRootExpansionValue, out var allowExpansion)
                                 && allowExpansion,
        };
    }
}
