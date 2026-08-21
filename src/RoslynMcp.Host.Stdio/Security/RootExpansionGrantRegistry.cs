using System.Collections.Concurrent;

namespace RoslynMcp.Host.Stdio.Security;

/// <summary>
/// <b>preview-apply-token-write-path-toctou:</b> records which loaded workspaces were admitted
/// under a one-level sanctioned-root expansion (<c>workspace_load(expandSanctionedRoots: true)</c>
/// combined with the server-owned <see cref="RoslynMcp.Roslyn.Services.SecurityOptions.AllowRootExpansion"/>).
/// </summary>
/// <remarks>
/// <para>
/// Redemption-time boundary revalidation must re-derive the SAME boundary that admitted the
/// workspace, never a narrower one. A sibling-worktree workspace loaded under a widened parent has
/// every document <c>FilePath</c> outside the configured roots proper; without this grant the
/// revalidation would refuse every <c>*_apply</c> on such a workspace — a functional regression on
/// a supported load mode, not a security win.
/// </para>
/// <para>
/// The grant is <b>sticky-true until close</b>: <c>workspace_load</c> is idempotent by path, so a
/// later plain load returning the same <c>WorkspaceId</c> must not silently downgrade a boundary
/// the workspace's documents already depend on. It never widens anything on its own — the effective
/// widening still requires the operator's <c>AllowRootExpansion</c>, which
/// <c>ClientRootPathValidator</c> ANDs in.
/// </para>
/// </remarks>
internal static class RootExpansionGrantRegistry
{
    private static readonly ConcurrentDictionary<string, byte> s_grants =
        new(StringComparer.Ordinal);

    /// <summary>Records that <paramref name="workspaceId"/> was loaded with root expansion requested.</summary>
    public static void Grant(string workspaceId)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            s_grants[workspaceId] = 0;
        }
    }

    /// <summary>
    /// Drops the grant for <paramref name="workspaceId"/>. Host composition registers this as a
    /// <c>WorkspaceClosed</c> handler so explicit close, LRU eviction, and disposal share one path.
    /// </summary>
    public static void Revoke(string workspaceId)
    {
        if (!string.IsNullOrEmpty(workspaceId))
        {
            s_grants.TryRemove(workspaceId, out _);
        }
    }

    /// <summary>
    /// True when <paramref name="workspaceId"/> was admitted under a requested root expansion.
    /// </summary>
    public static bool IsGranted(string? workspaceId)
        => !string.IsNullOrEmpty(workspaceId) && s_grants.ContainsKey(workspaceId);
}
