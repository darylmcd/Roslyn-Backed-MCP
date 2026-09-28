namespace RoslynMcp.Core.Services;

/// <summary>
/// Signals that a workspace lookup missed because the supplied <c>workspaceId</c> is unknown:
/// never issued by this host, or otherwise not classifiable as an eviction (see
/// <see cref="WorkspaceEvictedException"/>). Surfaced by <c>ToolErrorHandler</c> with the 4.x
/// wire values <c>category=NotFound</c> and <c>exceptionType=KeyNotFoundException</c> plus the
/// additive <c>reason=WorkspaceNotFound</c>, so callers can tell a bad workspace id apart from a
/// missing symbol, file, or metadata name (<c>NotFound</c> without a reason). A miss that races an
/// in-call auto-reload keeps the 4.x <c>category=WorkspaceReloadedDuringCall</c> with the same
/// reason and <c>exceptionType</c>.
/// </summary>
/// <remarks>
/// Derives from <see cref="System.Collections.Generic.KeyNotFoundException"/> so existing
/// <c>catch (KeyNotFoundException)</c> sites keep observing the lookup miss.
/// </remarks>
public sealed class WorkspaceNotFoundException : System.Collections.Generic.KeyNotFoundException
{
    /// <summary>Identifier of the workspace that was looked up.</summary>
    public string WorkspaceId { get; }

    /// <summary>Creates the exception for an unknown workspace id.</summary>
    public WorkspaceNotFoundException(string workspaceId, string message)
        : base(message)
    {
        WorkspaceId = workspaceId;
    }
}
