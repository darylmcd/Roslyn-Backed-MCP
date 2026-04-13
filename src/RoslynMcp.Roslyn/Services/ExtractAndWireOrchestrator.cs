using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class ExtractAndWireOrchestrator : IExtractAndWireOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;
    private readonly IPreviewStore _previewStore;
    private readonly ICrossProjectRefactoringService _crossProjectRefactoringService;
    private readonly IDiRegistrationService _diRegistrationService;

    public ExtractAndWireOrchestrator(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        IPreviewStore previewStore,
        ICrossProjectRefactoringService crossProjectRefactoringService,
        IDiRegistrationService diRegistrationService)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
        _previewStore = previewStore;
        _crossProjectRefactoringService = crossProjectRefactoringService;
        _diRegistrationService = diRegistrationService;
    }

    public async Task<RefactoringPreviewDto> PreviewExtractAndWireInterfaceAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string targetProjectName,
        bool updateDiRegistrations,
        CancellationToken ct)
    {
        var workspaceVersion = _workspace.GetCurrentVersion(workspaceId);
        var resolvedInterfaceName = string.IsNullOrWhiteSpace(interfaceName) ? $"I{typeName}" : interfaceName;
        var currentSolution = _workspace.GetCurrentSolution(workspaceId);

        var extractionPreview = await _crossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspaceId,
            filePath,
            typeName,
            resolvedInterfaceName,
            targetProjectName,
            ct).ConfigureAwait(false);

        var extractionEntry = _previewStore.Retrieve(extractionPreview.PreviewToken)
            ?? throw new InvalidOperationException("The intermediate extract-interface preview token could not be resolved.");
        var modifiedSolution = extractionEntry.ModifiedSolution;
        _previewStore.Invalidate(extractionPreview.PreviewToken);

        var mutations = await BuildMutationsFromSolutionAsync(currentSolution, modifiedSolution, ct).ConfigureAwait(false);
        var changes = (await SolutionDiffHelper.ComputeChangesAsync(currentSolution, modifiedSolution, ct).ConfigureAwait(false)).ToList();
        var warnings = extractionPreview.Warnings?.ToList() ?? [];

        if (updateDiRegistrations)
        {
            var registrations = await _diRegistrationService.GetDiRegistrationsAsync(workspaceId, null, ct).ConfigureAwait(false);
            var candidateFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in registrations.Where(registration =>
                         string.Equals(ShortTypeName(registration.ImplementationType), typeName, StringComparison.Ordinal) ||
                         string.Equals(ShortTypeName(registration.ServiceType), typeName, StringComparison.Ordinal)))
            {
                candidateFiles.Add(registration.FilePath);
            }

            foreach (var document in currentSolution.Projects.SelectMany(project => project.Documents))
            {
                if (!string.IsNullOrWhiteSpace(document.FilePath) &&
                    string.Equals(Path.GetExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    candidateFiles.Add(document.FilePath);
                }
            }

            foreach (var candidateFile in candidateFiles)
            {
                if (!File.Exists(candidateFile))
                {
                    continue;
                }

                var originalContent = await File.ReadAllTextAsync(candidateFile, ct).ConfigureAwait(false);
                var updatedContent = RewriteDiRegistration(originalContent, typeName, resolvedInterfaceName);
                if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
                {
                    continue;
                }

                UpsertMutation(mutations, candidateFile, updatedContent);
                changes.RemoveAll(change => string.Equals(change.FilePath, candidateFile, StringComparison.OrdinalIgnoreCase));
                changes.Add(new FileChangeDto(candidateFile, DiffGenerator.GenerateUnifiedDiff(originalContent, updatedContent, candidateFile)));
            }
        }

        if (mutations.Count == 0)
        {
            throw new InvalidOperationException("The extract-and-wire orchestration did not produce any file changes.");
        }

        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}' and wire consumers";
        var token = _compositePreviewStore.Store(workspaceId, workspaceVersion, description, mutations);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count == 0 ? null : warnings);
    }

    private static void UpsertMutation(List<CompositeFileMutation> mutations, string filePath, string updatedContent)
    {
        var existingIndex = mutations.FindIndex(mutation => string.Equals(mutation.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            mutations[existingIndex] = new CompositeFileMutation(filePath, updatedContent);
            return;
        }

        mutations.Add(new CompositeFileMutation(filePath, updatedContent));
    }

    private static string RewriteDiRegistration(string originalContent, string typeName, string interfaceName)
    {
        return originalContent
            .Replace($"AddSingleton<{typeName}>", $"AddSingleton<{interfaceName}, {typeName}>", StringComparison.Ordinal)
            .Replace($"AddScoped<{typeName}>", $"AddScoped<{interfaceName}, {typeName}>", StringComparison.Ordinal)
            .Replace($"AddTransient<{typeName}>", $"AddTransient<{interfaceName}, {typeName}>", StringComparison.Ordinal);
    }

    private static string ShortTypeName(string typeName)
    {
        var tickIndex = typeName.IndexOf('<');
        var trimmed = tickIndex >= 0 ? typeName[..tickIndex] : typeName;
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
    }

    private static async Task<List<CompositeFileMutation>> BuildMutationsFromSolutionAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        CancellationToken ct)
    {
        var mutations = new List<CompositeFileMutation>();
        var solutionChanges = modifiedSolution.GetChanges(currentSolution);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetAddedDocuments())
            {
                var document = modifiedSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                mutations.Add(new CompositeFileMutation(document.FilePath, text.ToString()));
            }

            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = modifiedSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                mutations.Add(new CompositeFileMutation(document.FilePath, text.ToString()));
            }

            foreach (var documentId in projectChange.GetRemovedDocuments())
            {
                var document = currentSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                mutations.Add(new CompositeFileMutation(document.FilePath, null, DeleteFile: true));
            }

            AppendProjectReferenceMutation(currentSolution, modifiedSolution, projectChange, mutations);
        }

        return mutations;
    }

    private static void AppendProjectReferenceMutation(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        List<CompositeFileMutation> mutations)
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

        var document = XDocument.Parse(File.ReadAllText(modifiedProject.FilePath), LoadOptions.PreserveWhitespace);
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
                    string.Equals(NormalizeInclude((string?)element.Attribute("Include")), NormalizeInclude(relativePath), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            OrchestrationMsBuildXml.GetOrCreateItemGroup(document, "ProjectReference")
                .Add(new XElement("ProjectReference", new XAttribute("Include", relativePath)));
            changed = true;
        }

        foreach (var projectReference in removedProjectReferences)
        {
            var referencedProject = currentSolution.GetProject(projectReference.ProjectId) ?? modifiedSolution.GetProject(projectReference.ProjectId);
            var targetFileName = Path.GetFileName(referencedProject?.FilePath);
            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                continue;
            }

            var element = document.Descendants("ProjectReference").FirstOrDefault(candidate =>
            {
                var include = (string?)candidate.Attribute("Include");
                return !string.IsNullOrWhiteSpace(include) &&
                       string.Equals(Path.GetFileName(include), targetFileName, StringComparison.OrdinalIgnoreCase);
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

        UpsertMutation(mutations, modifiedProject.FilePath, document.ToString(SaveOptions.DisableFormatting));
    }

    private static string NormalizeInclude(string? include)
    {
        return (include ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
