using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Authorizes this diff/planner's explicit physical IO, or requires already materialized
/// snapshots without invoking lazy text loaders. This context does not authorize or intercept
/// semantic operations performed by a producer before it requests the diff.</summary>
internal sealed class SolutionDiffReadContext
{
    private readonly IWorkspacePathAuthority? _authority;
    private readonly string? _workspaceId;

    private SolutionDiffReadContext(IWorkspacePathAuthority? authority, string? workspaceId)
    {
        _authority = authority;
        _workspaceId = workspaceId;
    }

    internal static SolutionDiffReadContext SnapshotOnly { get; } = new(null, null);

    internal static SolutionDiffReadContext ForWorkspace(IWorkspaceManager manager, string workspaceId) =>
        new(manager as IWorkspacePathAuthority
            ?? throw InvalidOperationErrors.Internal("The workspace does not provide physical preview read authorization."), workspaceId);

    internal async Task<(Solution Original, Solution Modified)> PrepareAsync(Solution original, Solution modified, CancellationToken ct)
    {
        var deltas = SolutionFileMutationPlanner.DocumentChanges(original, modified).ToArray();
        if (_authority is not null)
        {
            var graphChanges = modified.GetChanges(original);
            var paths = SolutionFileMutationPlanner.ChangedPaths(original, modified)
                .Concat(graphChanges.GetAddedProjects().Concat(graphChanges.GetRemovedProjects()).Select(project => project.FilePath).OfType<string>())
                .Distinct(FileSystemPath.Comparer).ToArray();
            if (deltas.Any(delta => delta.Old is { FilePath: null } || delta.New is { FilePath: null }))
                throw new PublicInvalidOperationException("Physical previews require a file path for every changed document.");
            await _authority.AuthorizePathsAsync(_workspaceId!, paths, ct).ConfigureAwait(false);
            foreach (var path in paths) WorkspaceManager.EnsurePhysicalPath(path);
            return (original, modified);
        }

        // An Adhoc snapshot may use an absolute-looking name. It is still never authority
        // to open that file, nor to invoke a lazy loader which might do physical IO.
        var changes = modified.GetChanges(original);
        if (changes.GetProjectChanges().Any(change => change.GetAddedProjectReferences().Any() || change.GetRemovedProjectReferences().Any()
                || change.NewProject.FilePath is not null && SolutionFileMutationPlanner.ChangedPaths(original, modified)
                    .Contains(change.NewProject.FilePath, FileSystemPath.Comparer))
            || (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any()) && original.FilePath is not null
            || changes.GetAddedProjects().Any(project => project.FilePath is not null))
            throw new PublicInvalidOperationException("Physical project graph diffs require an authorized workspace read context.");
        // The planner also inspects unchanged linked peers at each delta key to detect
        // conflicting prescriptions. Every such participant must be safe to read.
        var keys = deltas.SelectMany(delta => new[] { delta.Old, delta.New }).OfType<TextDocument>()
            .Select(document => document.FilePath ?? document.Name).ToHashSet(FileSystemPath.Comparer);
        foreach (var document in original.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments)
                     .Where(document => keys.Contains(document.FilePath ?? document.Name)).ToArray())
        {
            ct.ThrowIfCancellationRequested();
            original = Materialize(original, document);
        }
        foreach (var document in modified.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments)
                     .Where(document => keys.Contains(document.FilePath ?? document.Name)).ToArray())
        {
            ct.ThrowIfCancellationRequested();
            modified = Materialize(modified, document);
        }
        return (original, modified);
    }

    internal Task<byte[]?> ReadBytesAsync(string path, CancellationToken ct)
        => ReadAuthorizedAsync(path, PhysicalInputReader.ReadAllBytesAsync, ct);

    internal Task<FileContentDigest?> ReadDigestAsync(string path, CancellationToken ct)
        => ReadAuthorizedAsync(path, PhysicalInputReader.ReadDigestAsync, ct);

    private async Task<T?> ReadAuthorizedAsync<T>(string path, Func<string, CancellationToken, Task<T>> read, CancellationToken ct) where T : class
    {
        if (_authority is null) return null;
        await _authority.AuthorizePathsAsync(_workspaceId!, [path], ct).ConfigureAwait(false);
        WorkspaceManager.EnsurePhysicalPath(path);
        return await FileSnapshotCapture.ReadIfPresentAsync(path, read, ct).ConfigureAwait(false);
    }

    internal Task AuthorizePathsAsync(IReadOnlyList<string> paths, CancellationToken ct) => _authority is null
        ? throw new PublicInvalidOperationException("Snapshot-only diffs cannot authorize physical compiler-context inputs.")
        : _authority.AuthorizePathsAsync(_workspaceId!, paths, ct);

    private static Solution Materialize(Solution solution, TextDocument document)
    {
        if (!document.TryGetText(out var text))
            throw new PublicInvalidOperationException("Snapshot-only diffs require already materialized document text; lazy physical loaders are not permitted.");
        return SolutionFileMutationPlanner.DocumentKind(document) switch
        {
            "Compile" => solution.WithDocumentText(document.Id, text),
            "AdditionalFiles" => solution.WithAdditionalDocumentText(document.Id, text),
            _ => solution.WithAnalyzerConfigDocumentText(document.Id, text)
        };
    }
}
