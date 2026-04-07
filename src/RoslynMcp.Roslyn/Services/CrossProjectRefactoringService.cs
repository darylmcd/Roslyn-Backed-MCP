using System.Collections.Immutable;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Services;

public sealed class CrossProjectRefactoringService : ICrossProjectRefactoringService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public CrossProjectRefactoringService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewMoveTypeToProjectAsync(
        string workspaceId,
        string sourceFilePath,
        string typeName,
        string targetProjectName,
        string? targetNamespace,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, sourceFilePath)
            ?? throw new InvalidOperationException($"Document not found: {sourceFilePath}");
        var targetProject = ResolveProject(solution, targetProjectName);

        if (sourceDocument.Project.Id == targetProject.Id)
        {
            throw new InvalidOperationException("Source and target projects must be different for move-type-to-project.");
        }

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = FindTypeDeclaration(sourceRoot, typeName);
        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created for the source document.");
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Type '{typeName}' could not be resolved.");
        var sourceDocumentFilePath = sourceDocument.FilePath
            ?? throw new InvalidOperationException("Source document must have a file path on disk.");
        var targetProjectDirectory = Path.GetDirectoryName(targetProject.FilePath)
            ?? throw new InvalidOperationException("Target project must have a file path on disk.");

        var currentNamespace = GetContainingNamespace(typeSymbol);
        var resolvedTargetNamespace = string.IsNullOrWhiteSpace(targetNamespace) ? currentNamespace : targetNamespace;
        if (!string.Equals(currentNamespace, resolvedTargetNamespace, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Changing the namespace during move-type-to-project is not supported in the current implementation.");
        }

        var targetFilePath = Path.Combine(targetProjectDirectory, Path.GetFileName(sourceDocumentFilePath));
        if (File.Exists(targetFilePath))
        {
            throw new InvalidOperationException($"Target file already exists: {targetFilePath}");
        }

        var movedRoot = CreateCompilationUnitForMember(sourceRoot, typeDeclaration, resolvedTargetNamespace);
        var updatedSolution = solution.AddDocument(
            DocumentId.CreateNewId(targetProject.Id),
            Path.GetFileName(targetFilePath),
            SourceText.From(movedRoot.ToFullString()),
            filePath: targetFilePath);

        updatedSolution = EnsureProjectReference(updatedSolution, sourceDocument.Project.Id, targetProject.Id);

        var sourceNamespace = typeDeclaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
        SyntaxNode updatedSourceRoot;
        if (sourceNamespace is not null && sourceNamespace.Members.Count == 1 && sourceRoot.Members.Count == 1)
        {
            updatedSolution = updatedSolution.RemoveDocument(sourceDocument.Id);
        }
        else
        {
            updatedSourceRoot = sourceRoot.RemoveNode(typeDeclaration, SyntaxRemoveOptions.KeepExteriorTrivia)
                ?? throw new InvalidOperationException("Failed to remove the type declaration from the source document.");
            updatedSolution = updatedSolution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = $"Move type '{typeName}' to project '{targetProject.Name}'";
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string? targetProjectName,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");
        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = FindTypeDeclaration(sourceRoot, typeName);
        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created for the source document.");
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Type '{typeName}' could not be resolved.");

        if (typeSymbol.TypeKind is not TypeKind.Class and not TypeKind.Struct)
        {
            throw new InvalidOperationException("Extract interface currently supports classes and structs only.");
        }

        var resolvedInterfaceName = string.IsNullOrWhiteSpace(interfaceName) ? $"I{typeName}" : interfaceName;
        var targetProject = string.IsNullOrWhiteSpace(targetProjectName)
            ? sourceDocument.Project
            : ResolveProject(solution, targetProjectName);
        var targetProjectDirectory = Path.GetDirectoryName(targetProject.FilePath)
            ?? throw new InvalidOperationException("Target project must have a file path on disk.");

        var isCrossProject = targetProject.Id != sourceDocument.Project.Id;
        var namespaceName = isCrossProject
            ? DeriveTargetNamespace(targetProject)
            : GetContainingNamespace(typeSymbol);

        await DetectExistingTypeConflictAsync(solution, namespaceName, resolvedInterfaceName, ct).ConfigureAwait(false);

        var interfaceRoot = CreateInterfaceCompilationUnit(sourceRoot, typeSymbol, resolvedInterfaceName, namespaceName, isCrossProject);
        var interfaceDirectory = isCrossProject
            ? ResolvePreferredInterfaceSubdirectory(solution, targetProject, targetProjectDirectory)
            : targetProjectDirectory;
        var interfaceFilePath = Path.Combine(interfaceDirectory, resolvedInterfaceName + ".cs");
        if (File.Exists(interfaceFilePath))
        {
            throw new InvalidOperationException($"Target interface file already exists: {interfaceFilePath}");
        }

        var updatedTypeDeclaration = AddBaseType(typeDeclaration, resolvedInterfaceName);
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDeclaration, updatedTypeDeclaration);
        // BUG-004: Do not normalize whitespace on the entire compilation unit — that re-flows
        // unrelated code (collapses multi-line bodies, reshuffles format specifiers). ReplaceNode
        // preserves the surrounding source layout while still applying our targeted edit.

        var updatedSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot)
            .AddDocument(
                DocumentId.CreateNewId(targetProject.Id),
                resolvedInterfaceName + ".cs",
                SourceText.From(interfaceRoot.ToFullString()),
                filePath: interfaceFilePath);

        if (isCrossProject)
        {
            updatedSolution = EnsureProjectReference(updatedSolution, sourceDocument.Project.Id, targetProject.Id);
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}'";
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewDependencyInversionAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string interfaceProjectName,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");
        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = FindTypeDeclaration(sourceRoot, typeName);
        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created for the source document.");
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Type '{typeName}' could not be resolved.");

        var resolvedInterfaceName = string.IsNullOrWhiteSpace(interfaceName) ? $"I{typeName}" : interfaceName;
        var updatedSolution = await CreateInterfaceExtractionSolutionAsync(
            solution,
            sourceDocument,
            sourceRoot,
            typeDeclaration,
            typeSymbol,
            resolvedInterfaceName,
            interfaceProjectName,
            ct).ConfigureAwait(false);

        var updatedConcreteType = await SymbolResolver.ResolveByMetadataNameAsync(updatedSolution, GetMetadataName(typeSymbol), ct).ConfigureAwait(false) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Type '{typeName}' could not be resolved after interface extraction.");

        foreach (var project in updatedSolution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (root is null || model is null)
                {
                    continue;
                }

                var parameterReplacements = root.DescendantNodes()
                    .OfType<ParameterSyntax>()
                    .Where(parameter => parameter.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not null && parameter.Type is not null)
                    .Select(parameter => new
                    {
                        Parameter = parameter,
                        Type = model.GetTypeInfo(parameter.Type!, ct).Type
                    })
                    .Where(entry => SymbolEqualityComparer.Default.Equals(entry.Type, updatedConcreteType))
                    .ToArray();

                if (parameterReplacements.Length == 0)
                {
                    continue;
                }

                var updatedRoot = root.ReplaceNodes(
                    parameterReplacements.Select(entry => entry.Parameter),
                    (original, _) => original.WithType(SyntaxFactory.ParseTypeName(resolvedInterfaceName)));
                updatedSolution = updatedSolution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
            }
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}' and update constructor dependencies";
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static string ResolvePreferredInterfaceSubdirectory(Solution solution, Project targetProject, string projectDirectory)
    {
        var conventional = new[] { "Interfaces", "Abstractions", "Contracts" };
        foreach (var dir in conventional)
        {
            var path = Path.Combine(projectDirectory, dir);
            if (Directory.Exists(path))
                return path;
        }

        foreach (var doc in targetProject.Documents)
        {
            if (doc.FilePath is null || !doc.FilePath.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(projectDirectory, doc.FilePath);
            var firstSegment = rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (firstSegment is not null &&
                conventional.Any(s => string.Equals(s, firstSegment, StringComparison.OrdinalIgnoreCase)))
            {
                return Path.Combine(projectDirectory, firstSegment);
            }
        }

        return projectDirectory;
    }

    private static Project ResolveProject(Solution solution, string targetProjectName)
    {
        return solution.Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, targetProjectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, targetProjectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {targetProjectName}");
    }

    private static TypeDeclarationSyntax FindTypeDeclaration(CompilationUnitSyntax root, string typeName)
    {
        return root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in the source document.");
    }

    private static string GetContainingNamespace(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
    }

    private static string DeriveTargetNamespace(Project targetProject)
    {
        // Use the project's default namespace if available, otherwise fall back to the project name
        return targetProject.DefaultNamespace
            ?? targetProject.AssemblyName
            ?? targetProject.Name;
    }

    private static async Task DetectExistingTypeConflictAsync(
        Solution solution, string namespaceName, string typeName, CancellationToken ct)
    {
        var fullyQualifiedName = string.IsNullOrWhiteSpace(namespaceName)
            ? typeName
            : $"{namespaceName}.{typeName}";

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation?.GetTypeByMetadataName(fullyQualifiedName) is not null)
            {
                throw new InvalidOperationException(
                    $"Type '{typeName}' already exists in project '{project.Name}' " +
                    $"(namespace '{namespaceName}'). Choose a different interface name to avoid conflicts.");
            }
        }
    }

    private static CompilationUnitSyntax CreateCompilationUnitForMember(
        CompilationUnitSyntax sourceRoot,
        MemberDeclarationSyntax member,
        string namespaceName,
        bool filterUsings = false)
    {
        var usings = filterUsings
            ? FilterUsingsForMember(sourceRoot.Usings, member, crossProjectExtraction: filterUsings)
            : sourceRoot.Usings;

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(usings)
            .WithLeadingTrivia(sourceRoot.GetLeadingTrivia())
            .WithTrailingTrivia(sourceRoot.GetTrailingTrivia());

        MemberDeclarationSyntax namespacedMember = string.IsNullOrWhiteSpace(namespaceName)
            ? member
            : SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
                .WithMembers(SyntaxFactory.SingletonList(member));

        return compilationUnit.WithMembers(SyntaxFactory.SingletonList(namespacedMember)).NormalizeWhitespace();
    }

    private static SyntaxList<UsingDirectiveSyntax> FilterUsingsForMember(
        SyntaxList<UsingDirectiveSyntax> sourceUsings,
        MemberDeclarationSyntax member,
        bool crossProjectExtraction = false)
    {
        var memberText = member.ToFullString();

        // Collect all type names referenced in the interface member signatures
        var referencedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in member.DescendantNodes())
        {
            if (node is IdentifierNameSyntax id)
                referencedNames.Add(id.Identifier.Text);
            else if (node is GenericNameSyntax generic)
                referencedNames.Add(generic.Identifier.Text);
            else if (node is QualifiedNameSyntax qualified)
                referencedNames.Add(qualified.Right.Identifier.Text);
        }

        // Always include System if any types are referenced
        var filtered = sourceUsings.Where(u =>
        {
            var name = u.Name?.ToString();
            if (name is null) return false;
            if (string.Equals(name, "System.Collections.Concurrent", StringComparison.Ordinal) &&
                !memberText.Contains("Concurrent", StringComparison.Ordinal))
            {
                return false;
            }

            if (name is "System") return true;
            // Keep the using if its last segment matches any referenced name
            var lastSegment = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
            return referencedNames.Contains(lastSegment) ||
                   referencedNames.Any(r => name.EndsWith($".{r}", StringComparison.Ordinal));
        }).ToArray();

        // BUG-N3: In cross-project extraction, falling back to all source usings pulls package
        // namespaces (e.g. Microsoft.Data.Sqlite) that the target project does not reference.
        // Only use the full-source fallback for same-project generation.
        if (filtered.Length == 0 && referencedNames.Count > 0 && !crossProjectExtraction)
            return sourceUsings;

        return new SyntaxList<UsingDirectiveSyntax>(filtered);
    }

    private static CompilationUnitSyntax CreateInterfaceCompilationUnit(
        CompilationUnitSyntax sourceRoot,
        INamedTypeSymbol typeSymbol,
        string interfaceName,
        string namespaceName,
        bool filterUsings = false)
    {
        var interfaceMembers = typeSymbol.GetMembers()
            .Where(member => member.DeclaredAccessibility == Accessibility.Public && !member.IsStatic && !member.IsImplicitlyDeclared)
            .Select(CreateInterfaceMember)
            .Where(member => member is not null)
            .Cast<MemberDeclarationSyntax>()
            .ToArray();

        if (interfaceMembers.Length == 0)
        {
            throw new InvalidOperationException($"Type '{typeSymbol.Name}' does not have public instance members that can be extracted.");
        }

        // BUG-003: Match the source type's accessibility. A cross-project extraction MUST stay
        // public because the interface is moved into a different assembly and an internal
        // interface would not be visible to consumers; for in-project extraction we honor
        // internal/private types so we don't accidentally widen visibility. `filterUsings` is
        // true exactly when this is a cross-project extraction.
        var matchSourceAccessibility = !filterUsings;
        var accessibilityToken = matchSourceAccessibility
            ? GetAccessibilityToken(typeSymbol.DeclaredAccessibility)
            : SyntaxFactory.Token(SyntaxKind.PublicKeyword);

        var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(interfaceName)
            .AddModifiers(accessibilityToken)
            .WithMembers(SyntaxFactory.List(interfaceMembers));

        return CreateCompilationUnitForMember(sourceRoot, interfaceDeclaration, namespaceName, filterUsings);
    }

    /// <summary>
    /// Maps a Roslyn <see cref="Accessibility"/> to a C# modifier token used on generated
    /// interface declarations. Mirrors <c>InterfaceExtractionService.GetAccessibilityToken</c>.
    /// </summary>
    private static SyntaxToken GetAccessibilityToken(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Internal => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        Accessibility.ProtectedAndInternal => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        Accessibility.ProtectedOrInternal => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        _ => SyntaxFactory.Token(SyntaxKind.PublicKeyword),
    };

    private static MemberDeclarationSyntax? CreateInterfaceMember(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Ordinary } method => SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.ParseTypeName(method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
                    method.Name)
                .WithParameterList(CreateParameterList(method.Parameters))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
            IPropertySymbol property => SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.ParseTypeName(property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
                    property.Name)
                .WithAccessorList(CreateAccessorList(property.GetMethod is not null, property.SetMethod is not null)),
            _ => null
        };
    }

    private static ParameterListSyntax CreateParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters.Select(parameter =>
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                .WithType(SyntaxFactory.ParseTypeName(parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))))));
    }

    private static AccessorListSyntax CreateAccessorList(bool hasGetter, bool hasSetter)
    {
        var accessors = new List<AccessorDeclarationSyntax>();
        if (hasGetter)
        {
            accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        }

        if (hasSetter)
        {
            accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        }

        return SyntaxFactory.AccessorList(SyntaxFactory.List(accessors));
    }

    private static TypeDeclarationSyntax AddBaseType(TypeDeclarationSyntax declaration, string interfaceName)
    {
        if (declaration.BaseList?.Types.Any(baseType =>
                string.Equals(baseType.Type.ToString(), interfaceName, StringComparison.Ordinal)) == true)
        {
            return declaration;
        }

        var interfaceType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName));
        if (declaration.BaseList is null)
        {
            // Create new base list with proper spacing: " : IFoo"
            var colonToken = SyntaxFactory.Token(SyntaxKind.ColonToken)
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var baseList = SyntaxFactory.BaseList(colonToken, SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceType));
            return declaration.WithBaseList(baseList);
        }

        // Add to existing base list with proper comma spacing: ", IFoo"
        var existingTypes = declaration.BaseList.Types.ToList();
        existingTypes.Add(interfaceType);
        var separatedList = SyntaxFactory.SeparatedList<BaseTypeSyntax>(existingTypes,
            Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), existingTypes.Count - 1));
        return declaration.WithBaseList(declaration.BaseList.WithTypes(separatedList));
    }

    private static Solution EnsureProjectReference(Solution solution, ProjectId sourceProjectId, ProjectId targetProjectId)
    {
        var sourceProject = solution.GetProject(sourceProjectId)
            ?? throw new InvalidOperationException("Source project was not found in the updated solution.");

        if (sourceProject.ProjectReferences.Any(reference => reference.ProjectId == targetProjectId))
        {
            return solution;
        }

        return solution.AddProjectReference(sourceProjectId, new ProjectReference(targetProjectId));
    }

    private static async Task<Solution> CreateInterfaceExtractionSolutionAsync(
        Solution solution,
        Document sourceDocument,
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        string interfaceName,
        string targetProjectName,
        CancellationToken ct)
    {
        var targetProject = ResolveProject(solution, targetProjectName);
        var targetProjectDirectory = Path.GetDirectoryName(targetProject.FilePath)
            ?? throw new InvalidOperationException("Target project must have a file path on disk.");
        var isCrossProject = targetProject.Id != sourceDocument.Project.Id;
        var namespaceName = isCrossProject
            ? DeriveTargetNamespace(targetProject)
            : GetContainingNamespace(typeSymbol);

        await DetectExistingTypeConflictAsync(solution, namespaceName, interfaceName, ct).ConfigureAwait(false);

        var interfaceRoot = CreateInterfaceCompilationUnit(sourceRoot, typeSymbol, interfaceName, namespaceName, isCrossProject);
        var interfaceFileDirectory = isCrossProject
            ? ResolvePreferredInterfaceSubdirectory(solution, targetProject, targetProjectDirectory)
            : targetProjectDirectory;
        var interfaceFilePath = Path.Combine(interfaceFileDirectory, interfaceName + ".cs");
        if (File.Exists(interfaceFilePath))
        {
            throw new InvalidOperationException($"Target interface file already exists: {interfaceFilePath}");
        }

        var updatedTypeDeclaration = AddBaseType(typeDeclaration, interfaceName);
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDeclaration, updatedTypeDeclaration);
        if (updatedSourceRoot is CompilationUnitSyntax updatedCu)
            updatedSourceRoot = updatedCu.NormalizeWhitespace();

        var updatedSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot)
            .AddDocument(
                DocumentId.CreateNewId(targetProject.Id),
                interfaceName + ".cs",
                SourceText.From(interfaceRoot.ToFullString()),
                filePath: interfaceFilePath);

        if (isCrossProject)
        {
            updatedSolution = EnsureProjectReference(updatedSolution, sourceDocument.Project.Id, targetProject.Id);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return updatedSolution;
    }

    private static string GetMetadataName(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? typeSymbol.MetadataName
            : typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName;
    }
}
