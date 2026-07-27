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

    public DocumentSetPersistenceService(IWorkspaceManager workspace, ILogger logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<(bool Success, IReadOnlyList<string> AppliedFiles)> PersistAsync(
        string workspaceId,
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        var persistenceState = await CreatePersistenceStateAsync(currentSolution, ct).ConfigureAwait(false);

        try
        {
            await PersistCoreAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                solutionChanges,
                persistenceState,
                ct).ConfigureAwait(false);

            return (true, persistenceState.GetDistinctAppliedFiles());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to persist document set changes for workspace {WorkspaceId}", workspaceId);
            return (false, []);
        }
    }

    private async Task<DocumentSetPersistenceState> CreatePersistenceStateAsync(
        Solution currentSolution,
        CancellationToken ct)
    {
        var allCsprojSnapshots = await CsprojSemanticEquality.SnapshotProjectsAsync(
            currentSolution.Projects
                .Select(project => project.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))!,
            _logger,
            ct).ConfigureAwait(false);

        return new DocumentSetPersistenceState(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new List<string>(),
            allCsprojSnapshots);
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
                persistenceState.SdkProjectCsprojSnapshots,
                persistenceState.AppliedFiles,
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
        Dictionary<string, string> sdkProjectCsprojSnapshots,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        await PersistProjectReferenceChangesAsync(
            currentSolution,
            modifiedSolution,
            projectChange,
            appliedFiles,
            ct).ConfigureAwait(false);

        var addedDocuments = projectChange.GetAddedDocuments().ToList();
        await SnapshotSdkProjectCsprojAsync(
            modifiedSolution,
            projectChange.ProjectId,
            addedDocuments.Count > 0,
            sdkProjectCsprojSnapshots,
            ct).ConfigureAwait(false);

        await PersistAddedDocumentsAsync(modifiedSolution, addedDocuments, appliedFiles, ct).ConfigureAwait(false);
        await PersistChangedDocumentsAsync(
            modifiedSolution,
            projectChange.GetChangedDocuments(),
            appliedFiles,
            ct).ConfigureAwait(false);
        PersistRemovedDocuments(currentSolution, projectChange.GetRemovedDocuments(), appliedFiles);
    }

    private async Task SnapshotSdkProjectCsprojAsync(
        Solution modifiedSolution,
        ProjectId projectId,
        bool hasAddedDocuments,
        Dictionary<string, string> sdkProjectCsprojSnapshots,
        CancellationToken ct)
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

        var csprojBytes = await File.ReadAllTextAsync(project.FilePath, ct).ConfigureAwait(false);
        sdkProjectCsprojSnapshots[project.FilePath] = csprojBytes;
    }

    private static async Task PersistAddedDocumentsAsync(
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
                Directory.CreateDirectory(directory);
            }

            var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
            appliedFiles.Add(document.FilePath);
        }
    }

    private static async Task PersistChangedDocumentsAsync(
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
            await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
            appliedFiles.Add(document.FilePath);
        }
    }

    private static void PersistRemovedDocuments(
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

            if (File.Exists(document.FilePath))
            {
                File.Delete(document.FilePath);
            }

            appliedFiles.Add(document.FilePath);
        }
    }

    private async Task ApplyChangesAsync(
        string workspaceId,
        Solution modifiedSolution,
        Dictionary<string, string> sdkProjectCsprojSnapshots,
        IReadOnlyDictionary<string, string> allCsprojSnapshots,
        CancellationToken ct)
    {
        var applied = _workspace.TryApplyChanges(workspaceId, modifiedSolution);

        await RestoreSdkProjectSnapshotsAsync(sdkProjectCsprojSnapshots, ct).ConfigureAwait(false);
        await CsprojSemanticEquality.RestoreTriviaOnlyDriftAsync(
            allCsprojSnapshots,
            sdkProjectCsprojSnapshots.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            _logger,
            operationTag: "csproj-reserialization-msbuildworkspace",
            ct).ConfigureAwait(false);

        if (applied)
        {
            return;
        }

        _logger.LogInformation(
            "TryApplyChanges rejected document-set changes for {WorkspaceId}; falling back to full ReloadAsync.",
            workspaceId);
        await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
    }

    private async Task RestoreSdkProjectSnapshotsAsync(
        Dictionary<string, string> sdkProjectCsprojSnapshots,
        CancellationToken ct)
    {
        foreach (var (csprojPath, originalContent) in sdkProjectCsprojSnapshots)
        {
            try
            {
                var currentContent = await File.ReadAllTextAsync(csprojPath, ct).ConfigureAwait(false);
                if (!string.Equals(currentContent, originalContent, StringComparison.Ordinal))
                {
                    await File.WriteAllTextAsync(csprojPath, originalContent, ct).ConfigureAwait(false);
                    _logger.LogDebug(
                        "Restored SDK-style csproj {Path} after TryApplyChanges injected an explicit Compile item.",
                        csprojPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to restore SDK-style csproj snapshot for {Path}; the project may show a duplicate-Compile build error until manually edited.",
                    csprojPath);
            }
        }
    }

    private static async Task PersistProjectReferenceChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        var modifiedProject = modifiedSolution.GetProject(projectChange.ProjectId);
        if (modifiedProject?.FilePath is null || !File.Exists(modifiedProject.FilePath))
        {
            return;
        }

        var addedProjectReferences = projectChange.GetAddedProjectReferences().ToArray();
        var removedProjectReferences = projectChange.GetRemovedProjectReferences().ToArray();
        if (addedProjectReferences.Length == 0 && removedProjectReferences.Length == 0)
        {
            return;
        }

        var originalContent = await File.ReadAllTextAsync(modifiedProject.FilePath, ct).ConfigureAwait(false);
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

        await File.WriteAllTextAsync(
            modifiedProject.FilePath,
            document.ToString(SaveOptions.DisableFormatting),
            ct).ConfigureAwait(false);
        appliedFiles.Add(modifiedProject.FilePath);
    }

    private static string NormalizeInclude(string? include) =>
        (include ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private sealed class DocumentSetPersistenceState(
        Dictionary<string, string> sdkProjectCsprojSnapshots,
        List<string> appliedFiles,
        IReadOnlyDictionary<string, string> allCsprojSnapshots)
    {
        public Dictionary<string, string> SdkProjectCsprojSnapshots { get; } = sdkProjectCsprojSnapshots;
        public List<string> AppliedFiles { get; } = appliedFiles;
        public IReadOnlyDictionary<string, string> AllCsprojSnapshots { get; } = allCsprojSnapshots;

        public IReadOnlyList<string> GetDistinctAppliedFiles() =>
            AppliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
