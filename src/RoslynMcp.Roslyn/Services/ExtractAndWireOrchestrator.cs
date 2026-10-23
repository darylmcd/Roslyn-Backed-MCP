using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
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

        await DetectDuplicateInterfaceAsync(currentSolution, filePath, typeName, resolvedInterfaceName, ct).ConfigureAwait(false);

        var extractionPreview = await _crossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspaceId,
            filePath,
            typeName,
            resolvedInterfaceName,
            targetProjectName,
            ct).ConfigureAwait(false);

        var extractionEntry = _previewStore.Retrieve(extractionPreview.PreviewToken)
            ?? throw new InvalidOperationException("The intermediate extract-interface preview token could not be resolved.");
        _previewStore.Invalidate(extractionPreview.PreviewToken);
        if (!ReferenceEquals(extractionEntry.OriginalSolution, currentSolution)
            || extractionEntry.WorkspaceVersion != workspaceVersion || extractionEntry.WorkspaceId != workspaceId)
            throw new StaleWorkspaceTransitionException(workspaceId, "The intermediate extraction preview does not belong to the original snapshot.");
        RequireCurrent();
        var modifiedSolution = extractionEntry.ModifiedSolution;
        var addedInterfaceId = modifiedSolution.GetChanges(currentSolution).GetProjectChanges()
            .SelectMany(change => change.GetAddedDocuments()).Single();
        var interfaceReference = await ExtractedInterfaceReference.ReadAsync(
            modifiedSolution.GetDocument(addedInterfaceId)!, ct).ConfigureAwait(false);

        var warnings = extractionPreview.Warnings?.ToList() ?? [];

        var consumerProjects = new HashSet<ProjectId>();

        if (updateDiRegistrations)
        {
            var registrations = await _diRegistrationService.GetDiRegistrationsAsync(workspaceId, null, ct).ConfigureAwait(false);
            var candidateFiles = new HashSet<string>(FileSystemPath.Comparer);
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
                var documentIds = DocumentPathLookup.GetDocumentIds(modifiedSolution, candidateFile);
                if (documentIds.IsEmpty)
                    throw new PublicInvalidOperationException("A registration edit requires an owned source document.");
                var firstDocument = modifiedSolution.GetDocument(documentIds[0])!;
                var snapshotText = await firstDocument.GetTextAsync(ct).ConfigureAwait(false);
                var contentToRewrite = snapshotText.ToString();
                var updatedContent = RewriteDiRegistration(contentToRewrite, typeName, interfaceReference.ToString());
                if (string.Equals(contentToRewrite, updatedContent, StringComparison.Ordinal)) continue;
                foreach (var documentId in documentIds)
                {
                    var document = modifiedSolution.GetDocument(documentId)!;
                    var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                    if (!string.Equals(text.ToString(), contentToRewrite, StringComparison.Ordinal))
                        throw new PublicInvalidOperationException("Linked registration documents disagree on the reviewed source content.");
                    modifiedSolution = modifiedSolution.WithDocumentText(documentId,
                        SourceText.From(updatedContent, text.Encoding, text.ChecksumAlgorithm));
                    consumerProjects.Add(document.Project.Id);
                }
            }
        }

        if (consumerProjects.Count > 0)
        {
            var originalBindings = await OriginalNameBindings.CaptureAsync(modifiedSolution, ct).ConfigureAwait(false);
            modifiedSolution = originalBindings.Solution;
            foreach (var consumer in consumerProjects)
                modifiedSolution = ProjectGraphHelpers.EnsureProjectReference(modifiedSolution, consumer, addedInterfaceId.ProjectId);
            modifiedSolution = await originalBindings.RestoreAsync(modifiedSolution, ct).ConfigureAwait(false);
        }

        var plan = await SolutionDiffHelper.CreateMutationPlanAsync(currentSolution, modifiedSolution,
            SolutionDiffReadContext.ForWorkspace(_workspace, workspaceId), allowVirtualPaths: false, ct).ConfigureAwait(false);
        // One planner owns both persisted edits and displayed diffs, including linked-file
        // agreement and exact project-reference aliases/interop metadata.
        var mutations = plan.Select(mutation => new CompositeFileMutation(mutation.Path,
            mutation.After is null ? null : mutation.NewText, DeleteFile: mutation.After is null)).ToArray();
        var changes = SolutionDiffHelper.ComputeChanges(plan);

        if (mutations.Length == 0)
        {
            throw new InvalidOperationException("The extract-and-wire orchestration did not produce any file changes.");
        }

        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}' and wire consumers";
        RequireCurrent();
        PreviewDiffSafety.RequireComplete(changes);
        var token = _compositePreviewStore.Store(workspaceId, workspaceVersion, description, mutations);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count == 0 ? null : warnings);

        void RequireCurrent()
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(currentSolution, _workspace.GetCurrentSolution(workspaceId))
                || workspaceVersion != _workspace.GetCurrentVersion(workspaceId))
                throw new StaleWorkspaceTransitionException(workspaceId, "The workspace changed while composing the extraction preview.");
        }
    }

    /// <summary>
    /// Detects whether the target type already implements an interface with the same simple name
    /// as <paramref name="resolvedInterfaceName"/> (across any project). When found, throws with
    /// a structured message that directs the caller to use
    /// <c>extract_interface_cross_project_preview</c> instead.
    /// </summary>
    private static async Task DetectDuplicateInterfaceAsync(
        Solution solution,
        string filePath,
        string typeName,
        string resolvedInterfaceName,
        CancellationToken ct)
    {
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath);
        if (sourceDocument is null)
        {
            return;
        }

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
        if (sourceRoot is null)
        {
            return;
        }

        var typeDeclaration = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, typeName, StringComparison.Ordinal));
        if (typeDeclaration is null)
        {
            return;
        }

        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, ct) as INamedTypeSymbol;
        if (typeSymbol is null)
        {
            return;
        }

        var existingInterface = typeSymbol.Interfaces.FirstOrDefault(iface =>
            string.Equals(iface.Name, resolvedInterfaceName, StringComparison.Ordinal));

        if (existingInterface is not null)
        {
            var existingProject = solution.Projects.FirstOrDefault(project =>
                project.Id == existingInterface.ContainingAssembly?.Locations
                    .Select(loc => solution.GetDocument(loc.SourceTree)?.Project.Id)
                    .FirstOrDefault(id => id is not null));

            var projectHint = existingProject is not null ? $" (project '{existingProject.Name}')" : string.Empty;
            throw new InvalidOperationException(
                $"Type '{typeName}' already implements interface '{resolvedInterfaceName}'{projectHint}. " +
                $"extract_and_wire_interface_preview cannot create a duplicate. " +
                $"If you intended to extract a cross-project interface contract, use extract_interface_cross_project_preview instead.");
        }
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

}
