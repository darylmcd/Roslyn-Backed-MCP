using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Persists Roslyn document-set and project-reference changes while preserving
/// semantically unchanged project-file bytes.
/// </summary>
internal sealed class DocumentSetPersistenceService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger _logger;
    private readonly IDocumentSetFileSystem _fileSystem;

    public DocumentSetPersistenceService(IWorkspaceManager workspace, ILogger logger)
        : this(workspace, logger, PhysicalDocumentSetFileSystem.Instance)
    {
    }

    internal DocumentSetPersistenceService(
        IWorkspaceManager workspace,
        ILogger logger,
        IDocumentSetFileSystem fileSystem)
    {
        _workspace = workspace;
        _logger = logger;
        _fileSystem = fileSystem;
    }

    public async Task<(bool Success, IReadOnlyList<string> AppliedFiles)> PersistAsync(
        string workspaceId,
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        DocumentSetPersistenceState? persistenceState = null;

        try
        {
            // PersistCoreAsync writes document bytes before TryApplyChanges. Reject linked
            // document paths before the first snapshot or write, including imported files.
            WorkspaceManager.EnsureChangedDocumentPathsArePhysical(currentSolution, modifiedSolution);
            persistenceState = await CreatePersistenceStateAsync(
                currentSolution,
                modifiedSolution,
                solutionChanges,
                ct).ConfigureAwait(false);
            await PersistCoreAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                solutionChanges,
                persistenceState,
                ct).ConfigureAwait(false);

            return (true, persistenceState.GetDistinctAppliedFiles());
        }
        catch (Exception ex)
        {
            if (persistenceState is not null)
            {
                await RollBackAsync(
                    workspaceId,
                    persistenceState.FileSnapshots,
                    ex).ConfigureAwait(false);
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            if (ex is not IOException
                && ex is not UnauthorizedAccessException
                && ex is not InvalidOperationException)
            {
                throw;
            }

            _logger.LogWarning(ex, "Failed to persist document set changes for workspace {WorkspaceId}", workspaceId);
            return (false, []);
        }
    }

    private async Task<DocumentSetPersistenceState> CreatePersistenceStateAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        var allCsprojSnapshots = await CsprojSemanticEquality.SnapshotProjectsAsync(
            currentSolution.Projects
                .Select(project => project.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))!,
            _logger,
            ct).ConfigureAwait(false);
        // Resolve every physical project-file intent against the original snapshots before
        // document writes or MSBuildWorkspace effects can change the XML being planned.
        var projectFileMutations = ProjectReferenceMutationRenderer.Plan(
            currentSolution,
            modifiedSolution,
            allCsprojSnapshots,
            ct);
        var sdkProjectCsprojSnapshots = SnapshotSdkProjectCsprojs(solutionChanges, allCsprojSnapshots);
        var fileSnapshots = await SnapshotPotentiallyMutatedFilesAsync(
            currentSolution,
            modifiedSolution,
            solutionChanges,
            allCsprojSnapshots.Keys,
            ct).ConfigureAwait(false);

        return new DocumentSetPersistenceState(
            sdkProjectCsprojSnapshots,
            projectFileMutations.Select(mutation => mutation.Path).ToList(),
            allCsprojSnapshots,
            fileSnapshots,
            projectFileMutations);
    }

    private async Task PersistCoreAsync(
        string workspaceId,
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        DocumentSetPersistenceState persistenceState,
        CancellationToken ct)
    {
        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            await PersistProjectDocumentChangesAsync(
                currentSolution,
                modifiedSolution,
                projectChange,
                persistenceState,
                ct).ConfigureAwait(false);
        }

        await ApplyChangesAsync(
            workspaceId,
            modifiedSolution,
            persistenceState.SdkProjectCsprojSnapshots,
            persistenceState.AllCsprojSnapshots,
            persistenceState.ProjectFileMutations,
            ct).ConfigureAwait(false);
    }

    private async Task PersistProjectDocumentChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        DocumentSetPersistenceState persistenceState,
        CancellationToken ct)
    {
        var addedDocuments = projectChange.GetAddedDocuments().ToList();
        await PersistAddedDocumentsAsync(
            modifiedSolution,
            addedDocuments,
            persistenceState.AppliedFiles,
            ct).ConfigureAwait(false);
        await PersistChangedDocumentsAsync(
            modifiedSolution,
            projectChange.GetChangedDocuments(),
            persistenceState.AppliedFiles,
            ct).ConfigureAwait(false);
        PersistRemovedDocuments(
            currentSolution,
            projectChange.GetRemovedDocuments(),
            persistenceState.AppliedFiles);
    }

    private static Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> SnapshotSdkProjectCsprojs(
        SolutionChanges solutionChanges,
        IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> allCsprojSnapshots)
    {
        var sdkProjectCsprojSnapshots =
            new Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot>(FileSystemPath.Comparer);
        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            if (!projectChange.GetAddedDocuments().Any()
                || projectChange.NewProject.FilePath is not { } projectPath
                || sdkProjectCsprojSnapshots.ContainsKey(projectPath))
            {
                continue;
            }

            if (!allCsprojSnapshots.TryGetValue(projectPath, out var snapshot))
            {
                throw new IOException(
                    $"Could not snapshot project file '{projectPath}' before persistence.");
            }

            var document = XDocument.Parse(snapshot.Content, LoadOptions.PreserveWhitespace);
            if (ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(document))
            {
                sdkProjectCsprojSnapshots[projectPath] = snapshot;
            }
        }

        return sdkProjectCsprojSnapshots;
    }

    private async Task PersistAddedDocumentsAsync(
        Solution modifiedSolution,
        IReadOnlyCollection<DocumentId> addedDocuments,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        foreach (var documentId in addedDocuments)
        {
            var document = modifiedSolution.GetDocument(documentId);
            if (document?.FilePath is null)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(document.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            await _fileSystem.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
            appliedFiles.Add(document.FilePath);
        }
    }

    private async Task PersistChangedDocumentsAsync(
        Solution modifiedSolution,
        IEnumerable<DocumentId> changedDocuments,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        foreach (var documentId in changedDocuments)
        {
            var document = modifiedSolution.GetDocument(documentId);
            if (document?.FilePath is null)
            {
                continue;
            }

            var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            await _fileSystem.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
            appliedFiles.Add(document.FilePath);
        }
    }

    private void PersistRemovedDocuments(
        Solution currentSolution,
        IEnumerable<DocumentId> removedDocuments,
        List<string> appliedFiles)
    {
        foreach (var documentId in removedDocuments)
        {
            var document = currentSolution.GetDocument(documentId);
            if (document?.FilePath is null)
            {
                continue;
            }

            if (_fileSystem.FileExists(document.FilePath))
            {
                _fileSystem.DeleteFile(document.FilePath);
            }

            appliedFiles.Add(document.FilePath);
        }
    }

    private async Task ApplyChangesAsync(
        string workspaceId,
        Solution modifiedSolution,
        Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> sdkProjectCsprojSnapshots,
        IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> allCsprojSnapshots,
        IReadOnlyList<ProjectReferenceMutationRenderer.Mutation> projectFileMutations,
        CancellationToken ct)
    {
        // MSBuildWorkspace must see the original references while it applies its change set.
        var applied = _workspace.TryApplyChanges(workspaceId, modifiedSolution);
        var plannedPaths = projectFileMutations.Select(mutation => mutation.Path)
            .ToHashSet(FileSystemPath.Comparer);

        await RestoreSdkProjectSnapshotsAsync(sdkProjectCsprojSnapshots, plannedPaths, ct).ConfigureAwait(false);
        var skipTriviaPaths = sdkProjectCsprojSnapshots.Keys.ToHashSet(FileSystemPath.Comparer);
        skipTriviaPaths.UnionWith(plannedPaths);
        await CsprojSemanticEquality.RestoreTriviaOnlyDriftAsync(
            allCsprojSnapshots,
            skipTriviaPaths,
            _logger,
            operationTag: "csproj-reserialization-msbuildworkspace",
            ct: ct,
            throwOnFailure: true).ConfigureAwait(false);
        await PersistProjectFileMutationsAsync(projectFileMutations, ct).ConfigureAwait(false);

        if (!applied)
        {
            throw InvalidOperationErrors.Internal(
                $"TryApplyChanges rejected persisted document-set changes for workspace '{workspaceId}'.");
        }
    }

    private async Task PersistProjectFileMutationsAsync(
        IReadOnlyList<ProjectReferenceMutationRenderer.Mutation> projectFileMutations,
        CancellationToken ct)
    {
        foreach (var mutation in projectFileMutations)
        {
            var currentBytes = await _fileSystem.ReadAllBytesAsync(mutation.Path, ct).ConfigureAwait(false);
            if (!currentBytes.AsSpan().SequenceEqual(mutation.Modified))
            {
                await _fileSystem.WriteAllBytesAsync(mutation.Path, mutation.Modified, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RestoreSdkProjectSnapshotsAsync(
        Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> sdkProjectCsprojSnapshots,
        IReadOnlySet<string> plannedPaths,
        CancellationToken ct)
    {
        foreach (var (csprojPath, original) in sdkProjectCsprojSnapshots)
        {
            if (plannedPaths.Contains(csprojPath))
            {
                continue;
            }

            var currentBytes = await _fileSystem.ReadAllBytesAsync(csprojPath, ct).ConfigureAwait(false);
            if (!currentBytes.AsSpan().SequenceEqual(original.Bytes))
            {
                await _fileSystem.WriteAllBytesAsync(csprojPath, original.Bytes, ct).ConfigureAwait(false);
                _logger.LogDebug(
                    "Restored SDK-style csproj {Path} after TryApplyChanges injected an explicit Compile item.",
                    csprojPath);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, FileMutationSnapshot>> SnapshotPotentiallyMutatedFilesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        IEnumerable<string> allCsprojPaths,
        CancellationToken ct)
    {
        var paths = new HashSet<string>(allCsprojPaths, FileSystemPath.Comparer);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            AddPath(paths, currentSolution.GetProject(projectChange.ProjectId)?.FilePath);
            AddPath(paths, modifiedSolution.GetProject(projectChange.ProjectId)?.FilePath);

            foreach (var documentId in projectChange.GetAddedDocuments())
            {
                AddPath(paths, modifiedSolution.GetDocument(documentId)?.FilePath);
            }

            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                AddPath(paths, currentSolution.GetDocument(documentId)?.FilePath);
                AddPath(paths, modifiedSolution.GetDocument(documentId)?.FilePath);
            }

            foreach (var documentId in projectChange.GetRemovedDocuments())
            {
                AddPath(paths, currentSolution.GetDocument(documentId)?.FilePath);
            }
        }

        var snapshots = new Dictionary<string, FileMutationSnapshot>(FileSystemPath.Comparer);
        foreach (var path in paths.OrderBy(static path => path, FileSystemPath.Comparer))
        {
            if (_fileSystem.FileExists(path))
            {
                var bytes = await _fileSystem.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                snapshots[path] = new FileMutationSnapshot(Existed: true, Bytes: bytes);
            }
            else
            {
                snapshots[path] = new FileMutationSnapshot(Existed: false, Bytes: null);
            }
        }

        return snapshots;
    }

    private static void AddPath(ISet<string> paths, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            paths.Add(Path.GetFullPath(path));
        }
    }

    private async Task RollBackAsync(
        string workspaceId,
        IReadOnlyDictionary<string, FileMutationSnapshot> snapshots,
        Exception persistenceFailure)
    {
        var rollbackFailures = new List<Exception>();
        foreach (var (path, snapshot) in snapshots)
        {
            try
            {
                if (snapshot.Existed)
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        _fileSystem.CreateDirectory(directory);
                    }

                    await _fileSystem.WriteAllBytesAsync(
                        path,
                        snapshot.Bytes
                            ?? throw new InvalidOperationException(
                                $"Snapshot for existing file '{path}' did not contain bytes."),
                        CancellationToken.None).ConfigureAwait(false);
                }
                else if (_fileSystem.FileExists(path))
                {
                    _fileSystem.DeleteFile(path);
                }
            }
            catch (Exception ex)
            {
                rollbackFailures.Add(ex);
            }
        }

        try
        {
            await _workspace.ReloadAsync(workspaceId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            rollbackFailures.Add(ex);
        }

        if (rollbackFailures.Count == 0)
        {
            return;
        }

        rollbackFailures.Insert(0, persistenceFailure);
        throw new AggregateException(
            $"Document-set persistence failed and rollback could not fully restore workspace '{workspaceId}'.",
            rollbackFailures);
    }

    internal static bool ProjectReferenceTargetsPath(
        string? include,
        string projectDirectory,
        string targetProjectPath)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return false;
        }

        try
        {
            var includedProjectPath = Path.GetFullPath(include, projectDirectory);
            var normalizedTargetPath = Path.GetFullPath(targetProjectPath);
            return string.Equals(includedProjectPath, normalizedTargetPath, FileSystemPath.Comparison);
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            // MSBuild expressions such as $(SomeRoot) cannot be resolved safely without
            // evaluating the project. Treat them as non-matches rather than deleting a
            // similarly named reference.
            return false;
        }
    }

    private sealed class DocumentSetPersistenceState(
        Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> sdkProjectCsprojSnapshots,
        List<string> appliedFiles,
        IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> allCsprojSnapshots,
        IReadOnlyDictionary<string, FileMutationSnapshot> fileSnapshots,
        IReadOnlyList<ProjectReferenceMutationRenderer.Mutation> projectFileMutations)
    {
        public Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> SdkProjectCsprojSnapshots { get; } =
            sdkProjectCsprojSnapshots;
        public List<string> AppliedFiles { get; } = appliedFiles;
        public IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> AllCsprojSnapshots { get; } =
            allCsprojSnapshots;
        public IReadOnlyDictionary<string, FileMutationSnapshot> FileSnapshots { get; } = fileSnapshots;
        public IReadOnlyList<ProjectReferenceMutationRenderer.Mutation> ProjectFileMutations { get; } =
            projectFileMutations;

        public IReadOnlyList<string> GetDistinctAppliedFiles() =>
            AppliedFiles.Distinct(FileSystemPath.Comparer).ToList();
    }

    private sealed record FileMutationSnapshot(bool Existed, byte[]? Bytes);
}

internal interface IDocumentSetFileSystem
{
    bool FileExists(string path);
    void CreateDirectory(string path);
    void DeleteFile(string path);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct);
    Task<string> ReadAllTextAsync(string path, CancellationToken ct);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct);
    Task WriteAllTextAsync(string path, string content, CancellationToken ct);
}

internal sealed class PhysicalDocumentSetFileSystem : IDocumentSetFileSystem
{
    public static PhysicalDocumentSetFileSystem Instance { get; } = new();

    private PhysicalDocumentSetFileSystem()
    {
    }

    public bool FileExists(string path) => File.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteFile(string path) => File.Delete(path);
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) =>
        File.ReadAllBytesAsync(path, ct);
    public Task<string> ReadAllTextAsync(string path, CancellationToken ct) =>
        File.ReadAllTextAsync(path, ct);
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct) =>
        File.WriteAllBytesAsync(path, bytes, ct);
    public Task WriteAllTextAsync(string path, string content, CancellationToken ct) =>
        File.WriteAllTextAsync(path, content, ct);
}
