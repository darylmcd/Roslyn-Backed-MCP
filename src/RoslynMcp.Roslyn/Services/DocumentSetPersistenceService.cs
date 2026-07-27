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
        var fileSnapshots = await SnapshotPotentiallyMutatedFilesAsync(
            currentSolution,
            modifiedSolution,
            solutionChanges,
            allCsprojSnapshots.Keys,
            ct).ConfigureAwait(false);

        return new DocumentSetPersistenceState(
            new Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot>(StringComparer.OrdinalIgnoreCase),
            new List<string>(),
            allCsprojSnapshots,
            fileSnapshots);
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
            ct).ConfigureAwait(false);
    }

    private async Task PersistProjectDocumentChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        DocumentSetPersistenceState persistenceState,
        CancellationToken ct)
    {
        await PersistProjectReferenceChangesAsync(
            currentSolution,
            modifiedSolution,
            projectChange,
            persistenceState.AppliedFiles,
            ct).ConfigureAwait(false);

        var addedDocuments = projectChange.GetAddedDocuments().ToList();
        SnapshotSdkProjectCsproj(
            modifiedSolution,
            projectChange.ProjectId,
            addedDocuments.Count > 0,
            persistenceState.SdkProjectCsprojSnapshots,
            persistenceState.AllCsprojSnapshots);

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

    private void SnapshotSdkProjectCsproj(
        Solution modifiedSolution,
        ProjectId projectId,
        bool hasAddedDocuments,
        Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> sdkProjectCsprojSnapshots,
        IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> allCsprojSnapshots)
    {
        if (!hasAddedDocuments)
        {
            return;
        }

        var project = modifiedSolution.GetProject(projectId);
        if (project?.FilePath is null
            || sdkProjectCsprojSnapshots.ContainsKey(project.FilePath)
            || !ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(project.FilePath, _logger))
        {
            return;
        }

        if (!allCsprojSnapshots.TryGetValue(project.FilePath, out var snapshot))
        {
            throw new IOException(
                $"Could not snapshot SDK-style project file '{project.FilePath}' before persistence.");
        }

        sdkProjectCsprojSnapshots[project.FilePath] = snapshot;
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
        CancellationToken ct)
    {
        var applied = _workspace.TryApplyChanges(workspaceId, modifiedSolution);

        await RestoreSdkProjectSnapshotsAsync(sdkProjectCsprojSnapshots, ct).ConfigureAwait(false);
        await CsprojSemanticEquality.RestoreTriviaOnlyDriftAsync(
            allCsprojSnapshots,
            sdkProjectCsprojSnapshots.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            _logger,
            operationTag: "csproj-reserialization-msbuildworkspace",
            ct: ct,
            throwOnFailure: true).ConfigureAwait(false);

        if (!applied)
        {
            throw new InvalidOperationException(
                $"TryApplyChanges rejected persisted document-set changes for workspace '{workspaceId}'.");
        }
    }

    private async Task RestoreSdkProjectSnapshotsAsync(
        Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> sdkProjectCsprojSnapshots,
        CancellationToken ct)
    {
        foreach (var (csprojPath, original) in sdkProjectCsprojSnapshots)
        {
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

    private async Task PersistProjectReferenceChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        var modifiedProject = modifiedSolution.GetProject(projectChange.ProjectId);
        if (modifiedProject?.FilePath is null || !_fileSystem.FileExists(modifiedProject.FilePath))
        {
            return;
        }

        var addedProjectReferences = projectChange.GetAddedProjectReferences().ToArray();
        var removedProjectReferences = projectChange.GetRemovedProjectReferences().ToArray();
        if (addedProjectReferences.Length == 0 && removedProjectReferences.Length == 0)
        {
            return;
        }

        var originalContent = await _fileSystem
            .ReadAllTextAsync(modifiedProject.FilePath, ct)
            .ConfigureAwait(false);
        var document = XDocument.Parse(originalContent, LoadOptions.PreserveWhitespace);
        var projectDirectory = Path.GetDirectoryName(modifiedProject.FilePath)
            ?? throw new InvalidOperationException("Project file path must have a parent directory.");
        var changed = false;

        foreach (var projectReference in addedProjectReferences)
        {
            var referencedProject = modifiedSolution.GetProject(projectReference.ProjectId);
            if (referencedProject?.FilePath is null)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, referencedProject.FilePath);
            if (document.Descendants("ProjectReference").Any(element =>
                    string.Equals(
                        NormalizeInclude((string?)element.Attribute("Include")),
                        NormalizeInclude(relativePath),
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            OrchestrationMsBuildXml.GetOrCreateItemGroup(document, "ProjectReference")
                .Add(new XElement("ProjectReference", new XAttribute("Include", relativePath)));
            changed = true;
        }

        foreach (var projectReference in removedProjectReferences)
        {
            var referencedProject = currentSolution.GetProject(projectReference.ProjectId)
                ?? modifiedSolution.GetProject(projectReference.ProjectId);
            var targetFileName = Path.GetFileName(referencedProject?.FilePath);
            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                continue;
            }

            var element = document.Descendants("ProjectReference").FirstOrDefault(candidate =>
            {
                var include = (string?)candidate.Attribute("Include");
                return !string.IsNullOrWhiteSpace(include)
                       && string.Equals(
                           Path.GetFileName(include),
                           targetFileName,
                           StringComparison.OrdinalIgnoreCase);
            });

            if (element is null)
            {
                continue;
            }

            element.Remove();
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        await _fileSystem.WriteAllTextAsync(
            modifiedProject.FilePath,
            document.ToString(SaveOptions.DisableFormatting),
            ct).ConfigureAwait(false);
        appliedFiles.Add(modifiedProject.FilePath);
    }

    private async Task<IReadOnlyDictionary<string, FileMutationSnapshot>> SnapshotPotentiallyMutatedFilesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        IEnumerable<string> allCsprojPaths,
        CancellationToken ct)
    {
        var paths = new HashSet<string>(allCsprojPaths, StringComparer.OrdinalIgnoreCase);

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

        var snapshots = new Dictionary<string, FileMutationSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
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

    private static string NormalizeInclude(string? include) =>
        (include ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private sealed class DocumentSetPersistenceState(
        Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> sdkProjectCsprojSnapshots,
        List<string> appliedFiles,
        IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> allCsprojSnapshots,
        IReadOnlyDictionary<string, FileMutationSnapshot> fileSnapshots)
    {
        public Dictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> SdkProjectCsprojSnapshots { get; } =
            sdkProjectCsprojSnapshots;
        public List<string> AppliedFiles { get; } = appliedFiles;
        public IReadOnlyDictionary<string, CsprojSemanticEquality.ProjectFileSnapshot> AllCsprojSnapshots { get; } =
            allCsprojSnapshots;
        public IReadOnlyDictionary<string, FileMutationSnapshot> FileSnapshots { get; } = fileSnapshots;

        public IReadOnlyList<string> GetDistinctAppliedFiles() =>
            AppliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
