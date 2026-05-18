namespace RoslynMcp.Core.Services;

/// <summary>
/// Signals that an <c>*_apply</c> tool call attempted to redeem a preview token that
/// has been invalidated — most commonly because the workspace was auto-reloaded
/// (version bump) after the preview was created, dropping the stored snapshot via
/// <c>IPreviewStore.InvalidateOnVersionBump</c>. The structural distinction from
/// <see cref="System.Collections.Generic.KeyNotFoundException"/> is the
/// <c>PreviewTokenStale</c> error category surfaced by
/// <c>RoslynMcp.Host.Stdio.Tools.ToolErrorHandler</c>: callers can branch on the
/// envelope category to recover (re-issue the paired <c>*_preview</c> call against
/// the post-reload workspace) instead of guessing whether they mis-spelled the
/// <c>workspaceId</c> or the symbol itself is gone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> the <c>preview-token-stale-across-auto-reload</c> row
/// (gh #767, P1 firewallanalyzer audit) observed that a typical
/// preview → apply → preview → apply sequence — where the first apply triggers an
/// auto-reload that bumps the workspace version past the second preview's pinned
/// span — surfaced as a bare <see cref="System.Collections.Generic.KeyNotFoundException"/>
/// with <c>category=NotFound</c>. The shape was indistinguishable from "workspace not
/// found" or "symbol not found": agents had no machine-readable signal that the
/// correct recovery was "re-issue the paired <c>*_preview</c> call" rather than
/// "check the workspace id" or "re-resolve the symbol."
/// </para>
///
/// <para>
/// <b>Inheritance choice:</b> derives from <see cref="System.InvalidOperationException"/>
/// rather than <see cref="System.Collections.Generic.KeyNotFoundException"/>. This is a
/// deliberate departure from <c>WorkspaceEvictedException</c> (which derives from
/// <c>KeyNotFoundException</c> for catch-site compatibility with preexisting
/// <c>catch (KeyNotFoundException)</c> sites): no production code paths catch the
/// stale-token throw — both throw sites in
/// <c>ToolDispatch.ApplyByTokenAsync</c> and <c>ApplyWithVerifyTool.ApplyWithVerify</c>
/// surface directly to <c>ToolErrorHandler</c> via the tool-call boundary, so changing
/// the base type breaks no internal contracts. Using <c>InvalidOperationException</c>
/// keeps the "the preview entry is structurally absent" lookup-miss signal scoped to
/// <c>KeyNotFoundException</c> (workspace evictions, symbol miss) and gives the
/// stale-token state its own "the operation cannot proceed because the prerequisite
/// is no longer valid" semantic — closer to .NET conventions for "object was disposed"
/// or "the underlying state changed."
/// </para>
///
/// <para>
/// <b>Registration order:</b> because the type derives from
/// <see cref="System.InvalidOperationException"/>, <c>ToolErrorHandler</c>'s handler
/// dictionary MUST register this type BEFORE the generic
/// <see cref="System.InvalidOperationException"/> entry so the more-specific
/// <c>PreviewTokenStale</c> category wins on the dispatch walk
/// (<c>Type.IsAssignableFrom</c> matches in insertion order). The classifier's
/// <c>IsInvocationWrapper</c> short-circuit operates earlier in the pipeline, so
/// stale-token throws (which are NOT invocation wrappers — no inner exception, no
/// "invocation" in the type name) fall through to the dictionary walk normally.
/// </para>
///
/// <para>
/// <b>Two staleness lifecycles covered:</b>
/// </para>
/// <list type="bullet">
///   <item><description><b>Version-bump invalidation</b> — the dominant path: an
///     auto-reload via <c>WorkspaceManager.LoadIntoSessionAsync</c> calls
///     <c>IPreviewStore.InvalidateOnVersionBump</c>, which drops entries whose pinned
///     <c>[StoreVersion, StoreVersion + MaxVersionSpan]</c> range no longer covers
///     the new workspace version. The default <c>MaxVersionSpan = 1</c> means a single
///     bump is tolerated; a second concurrent reload pushes the ceiling past and the
///     token is dropped.</description></item>
///   <item><description><b>TTL expiration</b> — the secondary path: entries expire
///     after a fixed TTL regardless of version-bump activity. Composite preview stores
///     (<c>ICompositePreviewStore</c>, <c>IProjectMutationPreviewStore</c>) are NOT
///     wired to <c>InvalidateOnVersionBump</c>, so their tokens can only stale via TTL.
///     The envelope message uses "expired or invalidated" to cover both reasons
///     without forcing a per-store distinction at the throw site.</description></item>
/// </list>
///
/// <para>
/// This is a <b>retry-able</b> error in the same sense as
/// <see cref="StaleWorkspaceTransitionException"/>: callers re-issue the paired
/// <c>*_preview</c> against the current workspace state and immediately redeem the
/// fresh token. Distinct from <c>NotFound</c> (which implies the underlying entity is
/// permanently absent) and <c>WorkspaceEvicted</c> (which requires
/// <c>workspace_load</c> to rehydrate the session itself before any preview can be
/// issued).
/// </para>
/// </remarks>
public sealed class PreviewTokenStaleException : System.InvalidOperationException
{
    /// <summary>
    /// The opaque preview token that was rejected. Carried separately from
    /// <see cref="System.Exception.Message"/> so consumers can branch without
    /// parsing the message string — and so <c>ToolErrorHandler</c> can include it
    /// as a structured field in the error envelope.
    /// </summary>
    public string PreviewToken { get; }

    public PreviewTokenStaleException(string previewToken, string message, System.Exception? inner = null)
        : base(message, inner)
    {
        PreviewToken = previewToken;
    }
}
