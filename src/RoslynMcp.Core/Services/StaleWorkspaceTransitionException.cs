namespace RoslynMcp.Core.Services;

/// <summary>
/// Signals that a state-read operation on <c>WorkspaceManager</c> raced with (or followed)
/// an auto-reload transition and could not be served from a coherent snapshot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> the <c>autoreload-cascade-stdio-host-crash</c> regression
/// (Phase 6k batch on 2026-04-24) showed that two in-turn writers
/// (<c>preview_record_field_addition</c> + <c>extract_and_wire_interface_preview</c>) each
/// triggered <c>staleAction=auto-reloaded</c>, and the follow-up <c>workspace_changes</c>
/// reader raised an unhandled exception on the post-auto-reload state-read path that
/// terminated the stdio host (new <c>stdioPid</c>, every in-flight preview token lost).
/// </para>
///
/// <para>
/// The remediation: every <c>WorkspaceManager</c> state-read method that observes the
/// session in a transition window (<see cref="WorkspaceManager.LoadIntoSessionAsync"/>
/// actively running, or just-failed with <c>Workspace == null</c>) must throw this
/// exception instead of propagating a generic <see cref="System.InvalidOperationException"/>
/// or raw Roslyn/MSBuild exception. The host's <c>ToolErrorHandler</c> recognizes this
/// type by name and surfaces it as <c>category="StaleWorkspaceTransition"</c> with a
/// retry hint — callers can re-issue the read after calling <c>workspace_reload</c>
/// (or on the next turn when the reload has settled).
/// </para>
///
/// <para>
/// This is a <b>retry-able</b> error, distinct from <c>InvalidOperation</c> (which in
/// the stale-workspace path would previously have suggested <c>workspace_reload</c> but
/// did not differentiate a permanent invalid state from a transient transition).
/// </para>
/// </remarks>
public sealed class StaleWorkspaceTransitionException : Exception
{
    /// <summary>
    /// Identifier of the workspace that was in transition when the read failed.
    /// Carried separately from <see cref="Exception.Message"/> so the error envelope
    /// can surface it in <c>_meta</c> without re-parsing the message string.
    /// </summary>
    public string WorkspaceId { get; }

    public StaleWorkspaceTransitionException(string workspaceId, string message, Exception? inner = null)
        : base(message, inner)
    {
        WorkspaceId = workspaceId;
    }
}
