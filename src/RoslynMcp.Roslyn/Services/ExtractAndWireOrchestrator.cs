using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class ExtractAndWireOrchestrator : IExtractAndWireOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;
    private readonly CrossProjectRefactoringService _crossProjectRefactoringService;
    private readonly IDiRegistrationService _diRegistrationService;

    public ExtractAndWireOrchestrator(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        CrossProjectRefactoringService crossProjectRefactoringService,
        IDiRegistrationService diRegistrationService)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
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

        var modifiedSolution = await _crossProjectRefactoringService.BuildExtractInterfaceSolutionAsync(
            workspaceId, currentSolution, filePath, typeName, resolvedInterfaceName, targetProjectName, ct).ConfigureAwait(false);
        var generatedInterface = await ResolveGeneratedInterfaceAsync(currentSolution, modifiedSolution, resolvedInterfaceName, ct).ConfigureAwait(false);
        modifiedSolution = await InterfaceLinkedDocumentReconciler.ReconcileAsync(currentSolution, modifiedSolution, generatedInterface.ProjectId, ct).ConfigureAwait(false);

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
                var linkedIds = modifiedSolution.GetDocumentIdsWithFilePath(candidateFile).ToArray();
                if (linkedIds.Length == 0)
                {
                    if (File.Exists(candidateFile))
                    {
                        var diskContent = await File.ReadAllTextAsync(candidateFile, ct).ConfigureAwait(false);
                        if (RewriteDiRegistration(diskContent, typeName, generatedInterface.QualifiedName) != diskContent)
                            throw new PublicInvalidOperationException($"DI registration file '{candidateFile}' is not mapped to a C# document in the workspace.");
                    }
                    continue;
                }

                var documents = linkedIds.Select(id => modifiedSolution.GetDocument(id)!).ToArray();
                var texts = await Task.WhenAll(documents.Select(document => document.GetTextAsync(ct))).ConfigureAwait(false);
                var content = texts[0].ToString();
                if (texts.Skip(1).Any(text => text.ToString() != content))
                    throw new PublicInvalidOperationException($"Linked C# documents at '{candidateFile}' have divergent content.");
                var updatedContent = RewriteDiRegistration(content, typeName, generatedInterface.QualifiedName);
                if (updatedContent == content) continue;
                foreach (var document in documents)
                    modifiedSolution = modifiedSolution.WithDocumentText(document.Id, Microsoft.CodeAnalysis.Text.SourceText.From(updatedContent, texts[0].Encoding));
            }
        }

        modifiedSolution = await InterfaceLinkedDocumentReconciler.ReconcileAsync(currentSolution, modifiedSolution, generatedInterface.ProjectId, ct).ConfigureAwait(false);
        await InterfacePreviewCompilationGate.ValidateAsync(currentSolution, modifiedSolution, ct).ConfigureAwait(false);
        var mutations = await BuildMutationsFromSolutionAsync(currentSolution, modifiedSolution, ct).ConfigureAwait(false);
        var changes = await SolutionDiffHelper.ComputeChangesAsync(currentSolution, modifiedSolution, ct).ConfigureAwait(false);

        if (mutations.Count == 0)
        {
            throw new InvalidOperationException("The extract-and-wire orchestration did not produce any file changes.");
        }

        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}' and wire consumers";
        var token = _compositePreviewStore.Store(workspaceId, workspaceVersion, description, mutations);
        return new RefactoringPreviewDto(token, description, changes, null);
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

    private static void UpsertMutation(List<CompositeFileMutation> mutations, string filePath, string updatedContent)
    {
        var existingIndex = mutations.FindIndex(mutation => FileSystemPath.Comparer.Equals(mutation.FilePath, filePath));
        if (existingIndex >= 0)
        {
            if (mutations[existingIndex].UpdatedContent != updatedContent || mutations[existingIndex].DeleteFile)
                throw new PublicInvalidOperationException($"Linked C# documents at '{filePath}' have divergent mutations.");
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

    private static async Task<(string QualifiedName, ProjectId ProjectId)> ResolveGeneratedInterfaceAsync(
        Solution baseline, Solution pending, string interfaceName, CancellationToken ct)
    {
        foreach (var projectChange in pending.GetChanges(baseline).GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetAddedDocuments())
            {
                var document = pending.GetDocument(documentId)!;
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var declaration = root?.DescendantNodes().OfType<InterfaceDeclarationSyntax>()
                    .SingleOrDefault(candidate => candidate.Identifier.ValueText == interfaceName);
                if (declaration is null) continue;
                var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var symbol = model?.GetDeclaredSymbol(declaration, ct);
                if (symbol is not null)
                    return (symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), document.Project.Id);
            }
        }

        throw InvalidOperationErrors.Internal($"Generated interface '{interfaceName}' could not be resolved for DI registration rewriting.");
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
                if (document is null) throw InvalidOperationErrors.Internal("Added C# document could not be resolved.");
                var path = RequirePhysicalPath(document);

                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                UpsertMutation(mutations, path, text.ToString());
            }

            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = modifiedSolution.GetDocument(documentId);
                if (document is null) throw InvalidOperationErrors.Internal("Changed C# document could not be resolved.");
                var path = RequirePhysicalPath(document);

                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                UpsertMutation(mutations, path, text.ToString());
            }

            foreach (var documentId in projectChange.GetRemovedDocuments())
            {
                var document = currentSolution.GetDocument(documentId);
                if (document is null) throw InvalidOperationErrors.Internal("Removed C# document could not be resolved.");
                var path = RequirePhysicalPath(document);

                if (!modifiedSolution.GetDocumentIdsWithFilePath(path).Any())
                    mutations.Add(new CompositeFileMutation(path, null, DeleteFile: true));
            }

        }

        foreach (var mutation in await ProjectReferenceMutationRenderer.PlanAsync(currentSolution, modifiedSolution, ct).ConfigureAwait(false))
        {
            var content = CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content;
            UpsertMutation(mutations, mutation.Path, content);
        }

        return mutations;
    }

    private static string RequirePhysicalPath(Document document) =>
        !string.IsNullOrWhiteSpace(document.FilePath)
            ? document.FilePath
            : throw new PublicInvalidOperationException($"Changed C# document '{document.Name}' is not mapped to a physical file.");

}
