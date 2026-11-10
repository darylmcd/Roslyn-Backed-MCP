namespace RoslynMcp.Core.Services;

/// <summary>
/// The single owner of every structured-log event id and event name the server emits. Ids are plain
/// <see cref="int"/> constants (this assembly has no logging dependency); call sites build
/// <c>Microsoft.Extensions.Logging.EventId</c> from an <c>Id</c>/<c>Name</c> constant pair.
/// </summary>
/// <remarks>
/// Ranges (one per subsystem; a new event takes the next free id in its subsystem's range):
/// <list type="table">
/// <item><term>1-99</term><description>workspace lifecycle (<c>WorkspaceManager</c>)</description></item>
/// <item><term>1000-1999</term><description>host lifecycle and observability (unexpected failures, transport disconnects)</description></item>
/// <item><term>2000-2999</term><description>tool-call lifecycle (<c>StructuredResultProjector</c>)</description></item>
/// <item><term>3000-3999</term><description>process and command execution</description></item>
/// <item><term>4000-4999</term><description>Roslyn provider execution (code actions, fix enumeration)</description></item>
/// </list>
/// Ids already published in JSON-lines <c>eventId</c> values are stable and never renumbered.
/// <c>LogEventCatalogTests</c> pins every (id, name) pair, checks range membership, forbids literal ids in
/// <c>src/</c>, and compares this catalog to the table in <c>docs/stdio-client-integration.md</c>.
/// </remarks>
public static class LogEvents
{
    public const string WorkspaceSubsystem = "workspace";
    public const string HostSubsystem = "host";
    public const string ToolSubsystem = "tool";
    public const string ProcessSubsystem = "process";
    public const string ProviderSubsystem = "provider";

    // Workspace lifecycle: 1-99.
    public const int WorkspaceLoaded = 1;
    public const string WorkspaceLoadedName = "WorkspaceLoaded";
    public const int ChangesApplied = 2;
    public const string ChangesAppliedName = "ChangesApplied";
    public const int ChangesApplyFailed = 3;
    public const string ChangesApplyFailedName = "ChangesApplyFailed";
    public const int WorkspaceClosed = 4;
    public const string WorkspaceClosedName = "WorkspaceClosed";
    public const int SessionNotFound = 5;
    public const string SessionNotFoundName = "SessionNotFound";

    // Host lifecycle and observability: 1000-1999.
    public const int UnexpectedFailure = 1001;
    public const string UnexpectedFailureName = "UnexpectedFailure";
    public const int TransportDisconnected = 1002;
    public const string TransportDisconnectedName = "LogTransportDisconnected";

    // Tool-call lifecycle: 2000-2999.
    public const int ToolCompleted = 2101;
    public const string ToolCompletedName = "ToolCompleted";
    public const int ToolCancelled = 2102;
    public const string ToolCancelledName = "ToolCancelled";
    public const int ToolInputRequired = 2103;
    public const string ToolInputRequiredName = "ToolInputRequired";
    public const int ToolFailed = 2104;
    public const string ToolFailedName = "ToolFailed";

    // Process and command execution: 3000-3999.
    public const int CommandExecuted = 3001;
    public const string CommandExecutedName = "LogCommandExecuted";
    public const int ProcessTreeKillFailed = 3002;
    public const string ProcessTreeKillFailedName = "LogProcessTreeKillFailed";

    // Roslyn providers: 4000-4999.
    public const int ProviderExecutionFailed = 4001;
    public const string ProviderExecutionFailedName = "LogProviderExecutionFailed";
    public const int ProviderEnumerationFailed = 4002;
    public const string ProviderEnumerationFailedName = "LogProviderEnumerationFailed";

    /// <summary>Returns the subsystem owning <paramref name="id"/>, or null when it is outside every range.</summary>
    public static string? SubsystemOf(int id) => id switch
    {
        >= 1 and <= 99 => WorkspaceSubsystem,
        >= 1000 and <= 1999 => HostSubsystem,
        >= 2000 and <= 2999 => ToolSubsystem,
        >= 3000 and <= 3999 => ProcessSubsystem,
        >= 4000 and <= 4999 => ProviderSubsystem,
        _ => null,
    };
}
