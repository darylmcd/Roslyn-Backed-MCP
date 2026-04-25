using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// <c>mcp-connection-session-resilience</c> + <c>connection-state-ready-unsatisfiable-preload</c>
/// + <c>host-recycle-opacity</c>: canonical shape for the <c>connection</c> subfield emitted by
/// <c>server_info</c> and the <c>server_heartbeat</c> tool.
/// <para>
/// Consumers use this block to distinguish "transport reachable" from
/// "workspace-scoped tools will succeed":
/// </para>
/// <list type="bullet">
///   <item><description><c>idle</c> — the stdio transport is up and the server is fully initialized,
///     but no workspace has been loaded yet. This is a terminal pre-load state (NOT a transient
///     "initializing" step). Consumers must call <c>workspace_load</c> to advance to <c>ready</c>.</description></item>
///   <item><description><c>ready</c> — at least one workspace session is loaded; workspace-scoped
///     tools will resolve.</description></item>
///   <item><description><c>degraded</c> — reserved for future use when the server hit a startup
///     error but is still answering the protocol. Not emitted today.</description></item>
/// </list>
/// <para>
/// <strong>Recycle metadata (<c>host-recycle-opacity</c>):</strong> when the host stdio process is
/// recycled mid-session (idle eviction, watchdog timeout, client disconnect, etc.), the prior
/// process writes a small disk record on shutdown and the next host instance reads it on startup.
/// The first <c>server_info</c> / <c>server_heartbeat</c> probe after that restart carries
/// <see cref="PreviousStdioPid"/>, <see cref="PreviousExitedAtUtc"/>, and
/// <see cref="PreviousRecycleReason"/>, then the in-memory copy is cleared so subsequent probes
/// only ever surface those fields once. <c>previousRecycleReason</c> is one of:
/// <c>idle-eviction</c> / <c>watchdog</c> / <c>client-disconnect</c> / <c>graceful</c> /
/// <c>unknown</c>. Stale on-disk records (older than the configured TTL) are surfaced with
/// reason <c>unknown</c> rather than dropped silently — operators always get something actionable.
/// </para>
/// </summary>
public sealed record ConnectionStateDto(
    [property: JsonPropertyName("state")]
    string State,

    [property: JsonPropertyName("loadedWorkspaceCount")]
    int LoadedWorkspaceCount,

    [property: JsonPropertyName("stdioPid")]
    int StdioPid,

    [property: JsonPropertyName("serverStartedAt")]
    string ServerStartedAt,

    /// <summary>
    /// The PID of the previous host stdio process, captured from disk at startup and surfaced on
    /// the FIRST <c>server_info</c> / <c>server_heartbeat</c> probe of the new process. Null on
    /// every subsequent probe (consume-once semantics) and on a cold start with no prior record.
    /// </summary>
    [property: JsonPropertyName("previousStdioPid")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? PreviousStdioPid = null,

    /// <summary>
    /// ISO-8601 UTC timestamp when the previous host process recorded its shutdown. Surfaced
    /// once after a restart, alongside <see cref="PreviousStdioPid"/>. Null on subsequent probes
    /// and on cold starts with no prior record.
    /// </summary>
    [property: JsonPropertyName("previousExitedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PreviousExitedAtUtc = null,

    /// <summary>
    /// Why the previous process exited. Values: <c>graceful</c> (clean shutdown via
    /// <c>ApplicationStopping</c>), <c>idle-eviction</c>, <c>watchdog</c>, <c>client-disconnect</c>,
    /// <c>unknown</c> (record was stale or the previous process never wrote a reason — most likely
    /// a crash). Null on subsequent probes after the first one and on cold starts.
    /// </summary>
    [property: JsonPropertyName("previousRecycleReason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PreviousRecycleReason = null);
