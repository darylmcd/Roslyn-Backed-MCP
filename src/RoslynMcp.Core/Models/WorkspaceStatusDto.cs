using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the current status of a loaded workspace.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Staleness model</strong> (<c>workspace-stale-after-external-edit-feedback</c>):
/// <see cref="IsStale"/> is a boolean flag; <see cref="StaleReason"/> explains WHY the workspace
/// is stale so callers can choose the right remedy:
/// </para>
/// <list type="bullet">
///   <item><description><c>"external-edit"</c> — a tracked file changed on disk outside the
///     server's apply channel (e.g. an editor wrote a <c>.cs</c> file directly).
///     Write-preview tools (<c>change_signature_preview</c>, etc.) refuse in this state and
///     point callers at <c>workspace_reload</c>; auto-reload would silently swallow the
///     external drift.</description></item>
///   <item><description><c>"apply"</c> — the server itself just applied a preview. Auto-reload
///     on the next read is safe; this is the usual "settling" window.</description></item>
///   <item><description><c>"restore"</c> — an undo / revert just wrote files back to a prior
///     state. Auto-reload on the next read is safe.</description></item>
///   <item><description><c>null</c> — workspace is not stale.</description></item>
/// </list>
/// </remarks>
public sealed record WorkspaceStatusDto(
    string WorkspaceId,
    string? LoadedPath,
    int WorkspaceVersion,
    string SnapshotToken,
    DateTimeOffset LoadedAtUtc,
    int ProjectCount,
    int DocumentCount,
    IReadOnlyList<ProjectStatusDto> Projects,
    bool IsLoaded,
    bool IsStale,
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics,
    [property: JsonPropertyName("staleReason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? StaleReason = null,
    bool RestoreRequired = false);

/// <summary>
/// Represents the status of a project within a loaded workspace.
/// </summary>
public sealed record ProjectStatusDto(
    [property: JsonPropertyName("name")]
    string Name,
    string FilePath,
    int DocumentCount,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> TargetFrameworks,
    bool IsTestProject,
    string AssemblyName,
    string OutputType);
