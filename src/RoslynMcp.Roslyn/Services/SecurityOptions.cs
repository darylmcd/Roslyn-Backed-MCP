namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for security-related server behavior.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// When <c>false</c> (default), a roots lookup failure causes path validation to reject the
    /// request (fail-closed). When <c>true</c>, if the MCP client roots lookup fails the path is
    /// allowed unconditionally (fail-open).
    /// Set via <c>ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN</c> (true/false).
    /// </summary>
    public bool PathValidationFailOpen { get; init; }
}
