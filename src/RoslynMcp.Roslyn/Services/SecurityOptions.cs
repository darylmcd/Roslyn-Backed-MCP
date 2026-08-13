namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for security-related server behavior.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// Canonical server-owned file-system boundaries for path validation and bounded solution
    /// discovery. Values may be absolute or relative paths; consumers canonicalize them before
    /// comparison. An empty collection denies path access unless
    /// <see cref="PathValidationFailOpen"/> is explicitly enabled.
    /// </summary>
    public IReadOnlyList<string> SanctionedRoots { get; init; } = [];

    /// <summary>
    /// Allows a request that explicitly sets <c>expandSanctionedRoots</c> to widen each configured
    /// root by exactly one parent directory. This is disabled by default so request input alone
    /// can never widen the server-owned boundary. Set via
    /// <c>ROSLYNMCP_ALLOW_ROOT_EXPANSION</c> (true/false).
    /// </summary>
    public bool AllowRootExpansion { get; init; }

    /// <summary>
    /// When <c>false</c> (default), missing sanctioned-root configuration causes path validation
    /// to reject the request (fail-closed). When <c>true</c>, an empty configured boundary allows
    /// access as an explicit compatibility escape hatch. A non-empty configured boundary is
    /// always enforced; this option never permits a path outside it.
    /// Set via <c>ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN</c> (true/false).
    /// </summary>
    public bool PathValidationFailOpen { get; init; }
}
