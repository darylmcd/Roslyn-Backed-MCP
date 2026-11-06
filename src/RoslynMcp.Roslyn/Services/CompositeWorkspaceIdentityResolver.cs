using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// composite-token-portable-workspace-identity (ADR 0032): maps a persisted composite record onto
/// the workspace session of THIS host that should redeem it. A session id is a random per-process
/// GUID, so it names nothing on another host; the stable identity of a record is the normalized
/// path of the solution or project its session loaded.
/// </summary>
/// <remarks>
/// Only the disk-backed <see cref="CompositePreviewStore"/> uses this. Every refusal is a
/// <see cref="PublicInvalidOperationException"/> raised before the payload is claimed, so the
/// one-time token survives until a correctly loaded host redeems it. The workspace version is never
/// compared: it counts reloads of one session and means nothing across hosts.
/// </remarks>
public sealed class CompositeWorkspaceIdentityResolver
{
    private readonly IWorkspaceManager _workspaces;

    public CompositeWorkspaceIdentityResolver(IWorkspaceManager workspaces)
    {
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
    }

    /// <summary>
    /// The portable identity of a local session: its loaded solution/project path, normalized.
    /// Refuses a session that has no path (a record could never be redeemed elsewhere).
    /// </summary>
    public string IdentityOf(string workspaceId)
    {
        var loadedPath = _workspaces.GetStatus(workspaceId).LoadedPath;
        if (string.IsNullOrWhiteSpace(loadedPath))
        {
            throw new PublicInvalidOperationException(
                "The workspace has no loaded solution or project path, so a persisted composite preview cannot be bound to it. Load a .sln, .slnx or .csproj with workspace_load and re-issue the preview.");
        }

        return Path.GetFullPath(loadedPath);
    }

    /// <summary>
    /// Returns the id of the one local session that may redeem <paramref name="entry"/>.
    /// </summary>
    /// <exception cref="PublicInvalidOperationException">
    /// No session, or more than one, qualifies; the message names the corrective action.
    /// </exception>
    public string Resolve(CompositePreviewStore.Entry entry)
    {
        if (entry.WorkspaceIdentity is null)
        {
            // Format 1 carried only the creator's session id. That id is meaningful only on the
            // host that created it, so the record redeems there and nowhere else.
            return _workspaces.ContainsWorkspace(entry.WorkspaceId)
                ? entry.WorkspaceId
                : throw new PublicInvalidOperationException(
                    "This persisted composite preview was written by an older server version that bound it to its creating session, and that session is not live on this host. Redeem it on the creating host, or re-issue the paired *_preview call.");
        }

        var matches = _workspaces.ListWorkspaces()
            .Where(status => !string.IsNullOrWhiteSpace(status.LoadedPath)
                && FileSystemPath.Comparer.Equals(Path.GetFullPath(status.LoadedPath), entry.WorkspaceIdentity))
            .Select(status => status.WorkspaceId)
            .ToArray();

        // The creating session redeems its own record even when other sessions loaded the same path
        // (for example with different MSBuild global properties): its random id is itself proof.
        if (matches.Contains(entry.WorkspaceId, StringComparer.Ordinal))
        {
            return entry.WorkspaceId;
        }

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new PublicInvalidOperationException(
                "This persisted composite preview was created for a solution or project that is not loaded on this host. Load the same solution or project with workspace_load, then retry; the token has not been consumed."),
            _ => throw new PublicInvalidOperationException(
                $"This persisted composite preview matches {matches.Length} workspace sessions on this host (for example the same solution loaded with different global properties). Close the extra sessions so exactly one remains, then retry; the token has not been consumed."),
        };
    }
}
