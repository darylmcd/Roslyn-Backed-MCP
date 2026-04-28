namespace RoslynMcp.Core.Services;

/// <summary>
/// Signals that a workspace lookup missed because the session was evicted by a host
/// recycle (graceful shutdown of the prior MCP stdio process) or by an explicit
/// <c>workspace_close</c> call earlier in the same process. The structural distinction
/// from <see cref="System.Collections.Generic.KeyNotFoundException"/> is the
/// <c>WorkspaceEvicted</c> error category surfaced by
/// <c>RoslynMcp.Host.Stdio.Tools.ToolErrorHandler</c>: callers can branch on the
/// envelope category to recover (<c>workspace_load</c> rehydrates the prior
/// solution) instead of guessing whether they typo'd the <c>workspaceId</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> the <c>mcp-error-category-workspace-evicted-on-host-recycle</c>
/// row (self-audit on 2026-04-27) observed that every workspace-scoped tool call from
/// a prior session returned a bare <see cref="System.Collections.Generic.KeyNotFoundException"/>
/// with <c>category=NotFound</c> after a graceful host recycle (PID change with
/// <c>previousRecycleReason="graceful"</c>). The shape was indistinguishable from a
/// caller-typo'd <c>workspaceId</c> — both produced the same envelope. Agents had no
/// signal that "the prior process exited; rehydrate via workspace_load" was the
/// correct recovery rather than "double-check the id you sent."
/// </para>
///
/// <para>
/// <b>Inheritance choice:</b> derives from <see cref="System.Collections.Generic.KeyNotFoundException"/>
/// so existing <c>catch (KeyNotFoundException)</c> sites that swallow the lookup miss
/// (e.g. <c>WorkspaceExecutionGate.AutoReloadAsync</c> handling a workspace closed mid-call,
/// resource handlers in <c>WorkspaceResources</c>) continue to work. The
/// <c>ToolErrorHandler</c> classifier walks its handler dictionary in insertion order
/// with <c>Type.IsAssignableFrom</c>, so registering this type BEFORE the base
/// <c>KeyNotFoundException</c> entry makes the more-specific category win on the tool
/// path while preserving the catch-site contract for non-tool consumers.
/// </para>
///
/// <para>
/// <b>Two detection paths in <c>WorkspaceManager</c>:</b>
/// </para>
/// <list type="bullet">
///   <item><description><b>Same-process eviction</b> — <c>Close()</c> and <c>Dispose()</c>
///     populate an in-memory eviction record keyed by <c>workspaceId</c>, carrying the
///     original <c>loadedAt</c>. A subsequent lookup for that id throws this exception
///     with the recorded <c>WorkspaceLoadedAtUtc</c> populated.</description></item>
///   <item><description><b>Cross-process eviction (host recycle)</b> — <c>Program.cs</c>
///     publishes a recycle signal via <see cref="WorkspaceEvictionRegistry.PublishRecycleContext"/>
///     after reading the prior process's metadata. When a lookup misses inside a
///     freshly-started host with no live sessions and the registry reports a recycle,
///     this exception is thrown with <c>WorkspaceLoadedAtUtc=null</c> (the prior
///     <c>loadedAt</c> was lost with the prior process) and the registry-supplied
///     <see cref="ServerStartedAtUtc"/>.</description></item>
/// </list>
/// </remarks>
public sealed class WorkspaceEvictedException : System.Collections.Generic.KeyNotFoundException
{
    /// <summary>
    /// Identifier of the workspace that was looked up. Carried separately from
    /// <see cref="System.Exception.Message"/> so consumers can branch without parsing
    /// the message string.
    /// </summary>
    public string WorkspaceId { get; }

    /// <summary>
    /// UTC timestamp when the CURRENT host process started. Always populated; the
    /// envelope surfaces this so callers can correlate with <c>server_info</c>'s
    /// <c>connection.serverStartedAt</c> field and confirm the process identity.
    /// </summary>
    public DateTimeOffset ServerStartedAtUtc { get; }

    /// <summary>
    /// UTC timestamp when the workspace was originally loaded. Populated for
    /// same-process evictions where the manager retained the prior <c>loadedAt</c>;
    /// <see langword="null"/> for cross-process recycle evictions where the prior
    /// process's <c>loadedAt</c> was lost with the process. The envelope omits this
    /// field via <c>JsonIgnoreCondition.WhenWritingNull</c> when null so cold-start
    /// envelopes remain compact.
    /// </summary>
    public DateTimeOffset? WorkspaceLoadedAtUtc { get; }

    /// <summary>
    /// Constructs the exception for a same-process eviction where the original
    /// <c>loadedAt</c> is known.
    /// </summary>
    public WorkspaceEvictedException(
        string workspaceId,
        DateTimeOffset serverStartedAtUtc,
        DateTimeOffset workspaceLoadedAtUtc,
        string message)
        : base(message)
    {
        WorkspaceId = workspaceId;
        ServerStartedAtUtc = serverStartedAtUtc;
        WorkspaceLoadedAtUtc = workspaceLoadedAtUtc;
    }

    /// <summary>
    /// Constructs the exception for a cross-process recycle eviction where the prior
    /// session's <c>loadedAt</c> is unrecoverable.
    /// </summary>
    public WorkspaceEvictedException(
        string workspaceId,
        DateTimeOffset serverStartedAtUtc,
        string message)
        : base(message)
    {
        WorkspaceId = workspaceId;
        ServerStartedAtUtc = serverStartedAtUtc;
        WorkspaceLoadedAtUtc = null;
    }
}

/// <summary>
/// Process-wide publisher for the previous-host-process recycle context that
/// <c>WorkspaceManager</c> consults when a workspace lookup misses. Mirrors the
/// <c>HostProcessMetadataSnapshotProvider</c> pattern (in
/// <c>RoslynMcp.Host.Stdio.Diagnostics</c>) so the manager — which lives in
/// <c>RoslynMcp.Roslyn</c> — can read the signal without taking a layering-violating
/// dependency on the host-stdio assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle:</b> <c>Program.cs</c> reads the prior process's metadata via
/// <c>HostProcessMetadataStore.LoadPrevious()</c> and immediately calls
/// <see cref="PublishRecycleContext"/> with the result. The host-startup metadata
/// snapshot is consume-once for <c>server_info</c> / <c>server_heartbeat</c>
/// (per the existing snapshot-provider contract); the registry persists the recycle
/// SIGNAL for the lifetime of the process so every workspace lookup can consult it.
/// </para>
///
/// <para>
/// <b>Test paths:</b> production code calls <see cref="PublishRecycleContext"/> exactly
/// once at host startup. Tests stage a recycle context via the same call and call
/// <see cref="Reset"/> in their teardown to restore the cold-start state.
/// </para>
/// </remarks>
public static class WorkspaceEvictionRegistry
{
    private static readonly object s_lock = new();
    private static DateTimeOffset s_serverStartedAtUtc = DateTimeOffset.UtcNow;
    private static string? s_previousRecycleReason;

    /// <summary>
    /// UTC timestamp the current host process started. Defaulted to the static-ctor
    /// time so unit tests that construct <c>WorkspaceManager</c> without booting the
    /// full host get a non-null value. Production code overwrites this via
    /// <see cref="PublishRecycleContext"/> with the value sourced from
    /// <c>Process.StartTime.ToUniversalTime()</c> for accuracy.
    /// </summary>
    public static DateTimeOffset ServerStartedAtUtc
    {
        get { lock (s_lock) { return s_serverStartedAtUtc; } }
    }

    /// <summary>
    /// Recycle reason recorded by the prior host process at graceful shutdown
    /// (<c>"graceful"</c>) or normalized to <c>"unknown"</c> for stale records.
    /// <see langword="null"/> on a cold start (no prior process recorded).
    /// </summary>
    public static string? PreviousRecycleReason
    {
        get { lock (s_lock) { return s_previousRecycleReason; } }
    }

    /// <summary>
    /// <see langword="true"/> when <see cref="PreviousRecycleReason"/> is non-null —
    /// any prior-process recycle signal flips this on. Workspace lookups inside a
    /// freshly-started host that returns <see langword="true"/> here surface as
    /// <see cref="WorkspaceEvictedException"/> instead of bare
    /// <see cref="System.Collections.Generic.KeyNotFoundException"/> when the
    /// session set is empty (the prior process owned the now-missing ids).
    /// </summary>
    public static bool WasHostRecycled
    {
        get { lock (s_lock) { return s_previousRecycleReason is not null; } }
    }

    /// <summary>
    /// Publishes the current host process's start time and the prior process's recycle
    /// reason for <c>WorkspaceManager</c> to consult on workspace lookup misses. Called
    /// once at host startup from <c>Program.cs</c> immediately after reading the prior
    /// process's metadata.
    /// </summary>
    /// <param name="serverStartedAtUtc">When the current host process started.</param>
    /// <param name="previousRecycleReason">
    /// Recycle reason from the prior process's metadata file (e.g. <c>"graceful"</c>),
    /// or <see langword="null"/> on a cold start with no prior record.
    /// </param>
    public static void PublishRecycleContext(DateTimeOffset serverStartedAtUtc, string? previousRecycleReason)
    {
        lock (s_lock)
        {
            s_serverStartedAtUtc = serverStartedAtUtc;
            s_previousRecycleReason = previousRecycleReason;
        }
    }

    /// <summary>
    /// Test hook — restores the registry to its cold-start state (no recycle, default
    /// server-started timestamp). Production code should never call this.
    /// </summary>
    public static void Reset()
    {
        lock (s_lock)
        {
            s_serverStartedAtUtc = DateTimeOffset.UtcNow;
            s_previousRecycleReason = null;
        }
    }
}
