using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Default <see cref="IWorkspaceDriftService"/> implementation. Iterates every document
/// in the workspace's current <see cref="Microsoft.CodeAnalysis.Solution"/> and compares
/// the on-disk last-write time against the workspace's <see cref="WorkspaceStatusDto.LoadedAtUtc"/>
/// timestamp.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Out-of-band <c>Edit</c>/<c>Write</c> mutations (an editor or a
/// peer agent writing a <c>.cs</c> file directly) silently break Roslyn reads — the
/// workspace continues to serve the stale <see cref="Microsoft.CodeAnalysis.SourceText"/>
/// it loaded into memory at <c>workspace_load</c> time. Agents had two options before
/// this tool: always call <c>workspace_reload</c> before reads (slow) or never call it
/// (silent staleness). <see cref="CheckDriftAsync"/> turns that into a fast probe so the
/// reload only happens when it would actually change something.
/// </para>
/// <para>
/// <b>Drift definition.</b> A document is drifted when either (a) its on-disk
/// <see cref="System.IO.File.GetLastWriteTimeUtc"/> exceeds the workspace's
/// <c>LoadedAtUtc</c>, or (b) the file no longer exists on disk (treated as drift so
/// callers reload to surface the deletion through normal Roslyn diagnostics).
/// </para>
/// <para>
/// <b>Determinism.</b> The returned <see cref="WorkspaceDriftResult.FilesDrifted"/>
/// list is sorted ordinally so repeated calls on the same drifted state produce identical
/// JSON — important for snapshot tests and for agents that key on the list.
/// </para>
/// </remarks>
public sealed class WorkspaceDriftService : IWorkspaceDriftService
{
    private readonly IWorkspaceManager _workspace;

    public WorkspaceDriftService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public async Task<WorkspaceDriftResult> CheckDriftAsync(string workspaceId, CancellationToken ct)
    {
        var status = await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var loadedAtUtc = status.LoadedAtUtc.UtcDateTime;
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var drifted = new List<string>();

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;

            foreach (var document in project.Documents)
            {
                if (ct.IsCancellationRequested) break;

                var path = document.FilePath;
                if (string.IsNullOrEmpty(path))
                {
                    // In-memory documents (e.g. source-generated) have no file path —
                    // there is nothing on disk to drift against, skip cleanly.
                    continue;
                }

                if (!File.Exists(path))
                {
                    // Deleted on disk while still in the workspace's snapshot. Treat as
                    // drift so the caller reloads and the deletion surfaces through the
                    // normal Roslyn diagnostic path rather than as a stale read.
                    drifted.Add(path);
                    continue;
                }

                var mtime = File.GetLastWriteTimeUtc(path);
                if (mtime > loadedAtUtc)
                {
                    drifted.Add(path);
                }
            }
        }

        ct.ThrowIfCancellationRequested();

        // Dedupe (the same file can appear in multiple projects via linked documents) and
        // sort for deterministic output.
        var orderedUnique = drifted
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var stale = orderedUnique.Length > 0;
        return new WorkspaceDriftResult(
            Stale: stale,
            FilesDrifted: orderedUnique,
            Recommended: stale ? "reload" : "noop");
    }
}
