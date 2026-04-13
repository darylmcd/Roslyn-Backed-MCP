using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class OrchestrationService : IOrchestrationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;
    private readonly IPreviewStore _previewStore;
    private readonly ICrossProjectRefactoringService _crossProjectRefactoringService;
    private readonly IDiRegistrationService _diRegistrationService;
    private readonly IChangeTracker? _changeTracker;

    public OrchestrationService(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        IPreviewStore previewStore,
        ICrossProjectRefactoringService crossProjectRefactoringService,
        IDiRegistrationService diRegistrationService,
        IChangeTracker? changeTracker = null)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
        _previewStore = previewStore;
        _crossProjectRefactoringService = crossProjectRefactoringService;
        _diRegistrationService = diRegistrationService;
        _changeTracker = changeTracker;
    }

    public async Task<RefactoringPreviewDto> PreviewMigratePackageAsync(
        string workspaceId,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        CancellationToken ct)
    {
        var status = await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var workspaceVersion = _workspace.GetCurrentVersion(workspaceId);
        var packagesPropsPath = MsBuildMetadataHelper.FindDirectoryPackagesProps(status.LoadedPath);
        var usesCentralPackageManagement = packagesPropsPath is not null && MsBuildMetadataHelper.IsCentralPackageManagementEnabled(packagesPropsPath);
        var mutations = new List<CompositeFileMutation>();
        var changes = new List<FileChangeDto>();
        var warnings = new List<string>();

        foreach (var project in status.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
            {
                continue;
            }

            await BuildPackageReferenceEditAsync(
                project.FilePath, project.Name, oldPackageId, newPackageId, newVersion,
                usesCentralPackageManagement, mutations, changes, warnings, ct).ConfigureAwait(false);
        }

        if (usesCentralPackageManagement && packagesPropsPath is not null && File.Exists(packagesPropsPath))
        {
            await BuildCentralVersionEditAsync(
                packagesPropsPath, oldPackageId, newPackageId, newVersion, mutations, changes, ct).ConfigureAwait(false);
        }

        if (mutations.Count == 0)
        {
            throw new InvalidOperationException($"No project references to '{oldPackageId}' were found in the loaded workspace.");
        }

        var description = $"Migrate package '{oldPackageId}' to '{newPackageId}'";
        var token = _compositePreviewStore.Store(workspaceId, workspaceVersion, description, mutations);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count == 0 ? null : warnings);
    }

    /// <summary>
    /// Rewrites <c>PackageReference</c> entries inside one project file: removes the old
    /// package and adds the new one (with a Version attribute when CPM is not in use).
    /// Appends a <see cref="CompositeFileMutation"/> + <see cref="FileChangeDto"/> when the
    /// content actually changes; otherwise no-op. When the project already references the new
    /// package, only the old reference is removed and a warning is appended.
    /// </summary>
    private static async Task BuildPackageReferenceEditAsync(
        string projectFilePath,
        string projectName,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        bool usesCentralPackageManagement,
        List<CompositeFileMutation> mutations,
        List<FileChangeDto> changes,
        List<string> warnings,
        CancellationToken ct)
    {
        var originalContent = await File.ReadAllTextAsync(projectFilePath, ct).ConfigureAwait(false);
        var document = XDocument.Parse(originalContent, LoadOptions.PreserveWhitespace);
        var packageReferences = document.Descendants("PackageReference")
            .Where(element => string.Equals((string?)element.Attribute("Include"), oldPackageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (packageReferences.Length == 0)
        {
            return;
        }

        foreach (var packageReference in packageReferences)
        {
            packageReference.Remove();
        }

        if (document.Descendants("PackageReference").Any(element =>
                string.Equals((string?)element.Attribute("Include"), newPackageId, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Project '{projectName}' already referenced '{newPackageId}'. Removed '{oldPackageId}' only.");
        }
        else
        {
            var replacement = new XElement("PackageReference", new XAttribute("Include", newPackageId));
            if (!usesCentralPackageManagement)
            {
                replacement.Add(new XAttribute("Version", newVersion));
            }

            GetOrCreateItemGroup(document, "PackageReference").Add(replacement);
        }

        var updatedContent = document.ToString(SaveOptions.DisableFormatting);
        if (!string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            mutations.Add(new CompositeFileMutation(projectFilePath, updatedContent));
            changes.Add(new FileChangeDto(projectFilePath, DiffGenerator.GenerateUnifiedDiff(originalContent, updatedContent, projectFilePath)));
        }
    }

    /// <summary>
    /// Rewrites a <c>Directory.Packages.props</c> file: removes the old PackageVersion and
    /// upserts the new one with the requested version. Appends a <see cref="CompositeFileMutation"/>
    /// + <see cref="FileChangeDto"/> when the content actually changes.
    /// </summary>
    private static async Task BuildCentralVersionEditAsync(
        string packagesPropsPath,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        List<CompositeFileMutation> mutations,
        List<FileChangeDto> changes,
        CancellationToken ct)
    {
        var originalPropsContent = await File.ReadAllTextAsync(packagesPropsPath, ct).ConfigureAwait(false);
        var propsDocument = XDocument.Parse(originalPropsContent, LoadOptions.PreserveWhitespace);

        var oldCentralVersion = propsDocument.Descendants("PackageVersion")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Include"), oldPackageId, StringComparison.OrdinalIgnoreCase));
        oldCentralVersion?.Remove();

        UpsertCentralPackageVersion(propsDocument, newPackageId, newVersion);

        var updatedPropsContent = propsDocument.ToString(SaveOptions.DisableFormatting);
        if (!string.Equals(originalPropsContent, updatedPropsContent, StringComparison.Ordinal))
        {
            mutations.Add(new CompositeFileMutation(packagesPropsPath, updatedPropsContent));
            changes.Add(new FileChangeDto(packagesPropsPath, DiffGenerator.GenerateUnifiedDiff(originalPropsContent, updatedPropsContent, packagesPropsPath)));
        }
    }

    /// <summary>
    /// Sets the version of <paramref name="packageId"/> in the props document, creating the
    /// PackageVersion element if it does not already exist.
    /// </summary>
    private static void UpsertCentralPackageVersion(XDocument propsDocument, string packageId, string newVersion)
    {
        var existing = propsDocument.Descendants("PackageVersion")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            GetOrCreateItemGroup(propsDocument, "PackageVersion")
                .Add(new XElement("PackageVersion",
                    new XAttribute("Include", packageId),
                    new XAttribute("Version", newVersion)));
        }
        else
        {
            existing.SetAttributeValue("Version", newVersion);
        }
    }

    public async Task<RefactoringPreviewDto> PreviewSplitClassAsync(
        string workspaceId,
        string filePath,
        string typeName,
        IReadOnlyList<string> memberNames,
        string newFileName,
        CancellationToken ct)
    {
        if (memberNames.Count == 0)
        {
            throw new InvalidOperationException("Provide at least one member name to move into the partial class file.");
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in '{filePath}'.");

        var selectedMembers = typeDeclaration.Members
            .Where(member => GetMemberName(member) is string name && memberNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (selectedMembers.Length != memberNames.Count)
        {
            var foundNames = selectedMembers.Select(GetMemberName).Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
            var missingNames = memberNames.Where(name => !foundNames.Contains(name)).ToArray();
            throw new InvalidOperationException($"Member(s) not found in '{typeName}': {string.Join(", ", missingNames)}");
        }

        var partialOriginal = EnsurePartial(typeDeclaration.RemoveNodes(selectedMembers, SyntaxRemoveOptions.KeepExteriorTrivia)
            ?? throw new InvalidOperationException("Failed to remove the selected members from the original type."));
        var updatedRoot = root.ReplaceNode(typeDeclaration, partialOriginal);

        var partialNewType = EnsurePartial(typeDeclaration.WithMembers(SyntaxFactory.List(selectedMembers)));
        var namespaceName = GetNamespaceName(typeDeclaration);
        var partialCompilationUnit = CreateCompilationUnit(root, partialNewType, namespaceName);
        var newFilePath = Path.Combine(Path.GetDirectoryName(filePath)!, newFileName);
        // BUG-N4: Normalize generated trees only (not the whole solution) so file-scoped
        // namespace and partial/class keywords have guaranteed spacing.
        var newFileContent = partialCompilationUnit.NormalizeWhitespace().ToFullString();

        var originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var updatedRootNormalized = updatedRoot.NormalizeWhitespace();
        var updatedOriginalContent = updatedRootNormalized.ToFullString();
        var mutations = new List<CompositeFileMutation>
        {
            new(filePath, updatedOriginalContent),
            new(newFilePath, newFileContent)
        };
        var changes = new List<FileChangeDto>
        {
            new(filePath, DiffGenerator.GenerateUnifiedDiff(originalContent, updatedOriginalContent, filePath)),
            new(newFilePath, DiffGenerator.GenerateUnifiedDiff(string.Empty, newFileContent, newFilePath))
        };

        var description = $"Split class '{typeName}' into partial file '{newFileName}'";
        var token = _compositePreviewStore.Store(workspaceId, _workspace.GetCurrentVersion(workspaceId), description, mutations);
        return new RefactoringPreviewDto(token, description, changes, null);
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

    public async Task<ApplyResultDto> ApplyCompositeAsync(string previewToken, CancellationToken ct)
    {
        var entry = _compositePreviewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(false, [], "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, workspaceVersion, _, mutations) = entry.Value;
        if (_workspace.GetCurrentVersion(workspaceId) != workspaceVersion)
        {
            _compositePreviewStore.Invalidate(previewToken);
            return new ApplyResultDto(false, [], "Preview token is stale because the target workspace changed. Please create a new preview.");
        }

        var appliedFiles = new List<string>();

        try
        {
            foreach (var mutation in mutations)
            {
                if (mutation.DeleteFile)
                {
                    if (File.Exists(mutation.FilePath))
                    {
                        File.Delete(mutation.FilePath);
                    }

                    appliedFiles.Add(mutation.FilePath);
                    continue;
                }

                var directory = Path.GetDirectoryName(mutation.FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(mutation.FilePath, mutation.UpdatedContent ?? string.Empty, ct).ConfigureAwait(false);
                appliedFiles.Add(mutation.FilePath);
            }

            await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            _compositePreviewStore.Invalidate(previewToken);
            var distinctFiles = appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _changeTracker?.RecordChange(workspaceId, $"Composite operation ({distinctFiles.Count} files)", distinctFiles, "apply_composite_preview");
            return new ApplyResultDto(true, distinctFiles, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ApplyResultDto(false, appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ex.Message);
        }
    }

    private static XElement GetOrCreateItemGroup(XDocument document, string itemName)
    {
        var existingGroup = document.Root?.Elements("ItemGroup")
            .FirstOrDefault(group => group.Elements(itemName).Any());
        if (existingGroup is not null)
        {
            return existingGroup;
        }

        var itemGroup = new XElement("ItemGroup");
        document.Root?.Add(itemGroup);
        return itemGroup;
    }

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => null
        };
    }

    private static TypeDeclarationSyntax EnsurePartial(TypeDeclarationSyntax declaration)
    {
        if (declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            return declaration;

        // BUG-N4: Plain PartialKeyword token can glue to "class" in ToFullString(); keep trivia.
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.ElasticSpace);
        return declaration.WithModifiers(declaration.Modifiers.Add(partialToken));
    }

    private static string GetNamespaceName(TypeDeclarationSyntax declaration)
    {
        return declaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()?.Name.ToString() ?? string.Empty;
    }

    private static CompilationUnitSyntax CreateCompilationUnit(CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax declaration, string namespaceName)
    {
        var compilationUnit = SyntaxFactory.CompilationUnit().WithUsings(sourceRoot.Usings);
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(declaration));
        }

        var nsDecl = SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
            .WithNamespaceKeyword(
                SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(declaration));
        return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(nsDecl));
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

            GetOrCreateItemGroup(document, "ProjectReference")
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
