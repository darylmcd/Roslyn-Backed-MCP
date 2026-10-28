namespace RoslynMcp.Core.Services;

/// <summary>
/// Signals that a workspace lookup missed because the supplied <c>workspaceId</c> is unknown:
/// never issued by this host, or otherwise not classifiable as an eviction (see
/// <see cref="WorkspaceEvictedException"/>). Surfaced by <c>ToolErrorHandler</c> as
/// <c>category=WorkspaceNotFound</c> and <c>exceptionType=WorkspaceNotFoundException</c> (with the
/// deprecated <c>reason=WorkspaceNotFound</c> alias), on the plain path and when the miss races an
/// in-call auto-reload, so callers can tell a bad workspace id apart from a missing symbol, file,
/// or metadata name (<c>NotFound</c>).
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
