using System.Collections.Immutable;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Services;

public sealed class CrossProjectRefactoringService : ICrossProjectRefactoringService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ICompilationCache _compilationCache;

    public CrossProjectRefactoringService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ICompilationCache compilationCache)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _compilationCache = compilationCache;
    }

    public async Task<RefactoringPreviewDto> PreviewMoveTypeToProjectAsync(
        string workspaceId,
        string sourceFilePath,
        string typeName,
        string targetProjectName,
        string? targetNamespace,
        CancellationToken ct,
        bool preserveNamespace = false)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, sourceFilePath)
            ?? throw new InvalidOperationException($"Document not found: {sourceFilePath}");
        var targetProject = ResolveProject(solution, targetProjectName);

        if (sourceDocument.Project.Id == targetProject.Id)
        {
            throw new PublicInvalidOperationException("Source and target projects must be different for move-type-to-project.");
        }

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = FindTypeDeclaration(sourceRoot, typeName);
        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created for the source document.");
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, ct) as INamedTypeSymbol
            ?? throw new PublicInvalidOperationException($"Type '{typeName}' could not be resolved.");
        var sourceDocumentFilePath = sourceDocument.FilePath
            ?? throw new InvalidOperationException("Source document must have a file path on disk.");
        var targetProjectDirectory = Path.GetDirectoryName(targetProject.FilePath)
            ?? throw new InvalidOperationException("Target project must have a file path on disk.");

        var currentNamespace = GetContainingNamespace(typeSymbol);
        string resolvedTargetNamespace;
        if (!string.IsNullOrWhiteSpace(targetNamespace))
        {
            resolvedTargetNamespace = targetNamespace.Trim();
        }
        else if (preserveNamespace)
        {
            resolvedTargetNamespace = currentNamespace;
        }
        else
        {
            resolvedTargetNamespace = DeriveTargetNamespace(targetProject);
        }

        var targetFilePath = Path.Combine(targetProjectDirectory, Path.GetFileName(sourceDocumentFilePath));
        if (File.Exists(targetFilePath))
        {
            throw new PublicInvalidOperationException($"Target file already exists: {DescribeProjectRelativePath(targetProjectDirectory, targetFilePath)}");
        }

        var destinationNamespace = ReceivingNamespaceFactory.Create(resolvedTargetNamespace,
            ReceivingNamespaceFactory.RequireCSharpOptions(targetProject.ParseOptions));
        destinationNamespace = destinationNamespace switch
        {
            FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped
                .WithNamespaceKeyword(SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)),
            NamespaceDeclarationSyntax block => block
                .WithNamespaceKeyword(SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")))
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)),
            _ => destinationNamespace
        };
        var movedRoot = MovedDeclarationFileWriter.CreateCompilationUnit(
            sourceRoot, typeDeclaration, sourceRoot.Usings, destinationNamespace, semanticModel, ct);
        if (movedRoot.Members.Single() is NamespaceDeclarationSyntax movedNamespace &&
            !movedNamespace.Members.Last().ToFullString().EndsWith('\n'))
        {
            // A final line comment must end before the new namespace's closing brace.
            movedRoot = movedRoot.ReplaceNode(movedNamespace, movedNamespace.WithCloseBraceToken(
                movedNamespace.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)));
        }
        // Item #1 — pass folders so MSBuildWorkspace.TryApplyChanges resolves the disk path
        // consistently with our explicit write. Without folders, Roslyn resolves cross-project
        // adds to {targetProjectDir}/{fileName} and produces a rogue project-root copy.
        var movedDocFolders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, targetFilePath);
        var updatedSolution = solution.AddDocument(
            DocumentId.CreateNewId(targetProject.Id),
            Path.GetFileName(targetFilePath),
            SourceText.From(movedRoot.ToFullString()),
            folders: movedDocFolders,
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
            updatedSourceRoot = MovedDeclarationFileWriter.RemoveDeclaration(sourceRoot, typeDeclaration);
            updatedSolution = updatedSolution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = $"Move type '{typeName}' to project '{targetProject.Name}'";
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);
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
            ?? throw new PublicInvalidOperationException($"Type '{typeName}' could not be resolved.");

        if (typeSymbol.TypeKind is not TypeKind.Class and not TypeKind.Struct)
        {
            throw new PublicInvalidOperationException("Extract interface currently supports classes and structs only.");
        }

        var resolvedInterfaceName = string.IsNullOrWhiteSpace(interfaceName) ? $"I{typeName}" : interfaceName;
        var targetProject = string.IsNullOrWhiteSpace(targetProjectName)
            ? sourceDocument.Project
            : ResolveProject(solution, targetProjectName);
        var targetProjectDirectory = Path.GetDirectoryName(targetProject.FilePath)
            ?? throw new InvalidOperationException("Target project must have a file path on disk.");

        var isCrossProject = targetProject.Id != sourceDocument.Project.Id;

        // FLAG-10C: Resolve the interface subdirectory FIRST so the namespace can match the
        // file's actual on-disk location. Previously the namespace was derived purely from the
        // project default, producing files at e.g. src/Foo.Domain/Interfaces/IBar.cs that declared
        // namespace Foo.Domain — inconsistent with sibling files like ISnapshotStore that live in
        // Foo.Domain.Interfaces.
        var interfaceDirectory = isCrossProject
            ? ResolvePreferredInterfaceSubdirectory(solution, targetProject, targetProjectDirectory)
            : targetProjectDirectory;

        var namespaceName = isCrossProject
            ? DeriveTargetNamespaceForPath(targetProject, targetProjectDirectory, interfaceDirectory)
            : GetContainingNamespace(typeSymbol);

        await DetectExistingTypeConflictAsync(
            workspaceId, _compilationCache, solution, namespaceName, resolvedInterfaceName, ct).ConfigureAwait(false);

        // gh #765 — the projector records every type the projected members refer to; the generated
        // interface file's using block is built from those symbols (plus aliases/static/global from
        // the source file) instead of a text-grep that dropped source-project usings.
        var nullableProjection = new InterfaceNullableProjection();
        var projector = await InterfaceMemberProjector.CreateAsync(sourceDocument, nullableProjection, ct).ConfigureAwait(false);
        var interfaceRoot = CreateInterfaceCompilationUnit(sourceRoot, typeSymbol, resolvedInterfaceName, namespaceName, isCrossProject,
            ReceivingNamespaceFactory.RequireCSharpOptions(targetProject.ParseOptions), projector);
        var interfaceFilePath = Path.Combine(interfaceDirectory, resolvedInterfaceName + ".cs");
        if (File.Exists(interfaceFilePath))
        {
            throw new PublicInvalidOperationException($"Target interface file already exists: {DescribeProjectRelativePath(targetProjectDirectory, interfaceFilePath)}");
        }

        var updatedTypeDeclaration = InterfaceBaseTypeWriter.AddBaseType(typeDeclaration, resolvedInterfaceName, multilineContinuation: true);
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDeclaration, updatedTypeDeclaration);
        // BUG-004: Do not normalize whitespace on the entire compilation unit — that re-flows
        // unrelated code (collapses multi-line bodies, reshuffles format specifiers). ReplaceNode
        // preserves the surrounding source layout while still applying our targeted edit.

        // Item #1 — pass folders so MSBuildWorkspace resolves the disk path consistently.
        var interfaceFolders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, interfaceFilePath);
        var interfaceDocumentId = DocumentId.CreateNewId(targetProject.Id);
        var updatedSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot)
            .AddDocument(
                interfaceDocumentId,
                resolvedInterfaceName + ".cs",
                SourceText.From(interfaceRoot.ToFullString()),
                folders: interfaceFolders,
                filePath: interfaceFilePath);

        if (isCrossProject)
        {
            updatedSolution = EnsureProjectReference(updatedSolution, sourceDocument.Project.Id, targetProject.Id);
        }

        updatedSolution = await nullableProjection.ApplyAsync(updatedSolution.GetDocument(interfaceDocumentId)
            ?? throw InvalidOperationErrors.Internal("Generated interface document is unavailable."), interfaceRoot, ct).ConfigureAwait(false);
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}'";
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);
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
            ?? throw new PublicInvalidOperationException($"Type '{typeName}' could not be resolved.");

        var resolvedInterfaceName = string.IsNullOrWhiteSpace(interfaceName) ? $"I{typeName}" : interfaceName;
        var updatedSolution = await CreateInterfaceExtractionSolutionAsync(
            workspaceId,
            _compilationCache,
            solution,
            sourceDocument,
            sourceRoot,
            typeDeclaration,
            typeSymbol,
            resolvedInterfaceName,
            interfaceProjectName,
            ct).ConfigureAwait(false);

        var updatedConcreteType = await SymbolResolver.ResolveByMetadataNameAsync(updatedSolution, GetMetadataName(typeSymbol), ct).ConfigureAwait(false) as INamedTypeSymbol
            ?? throw new PublicInvalidOperationException($"Type '{typeName}' could not be resolved after interface extraction.");

        // Only documents that actually reference the extracted concrete type can hold a
        // constructor parameter typed at it. Use Roslyn's reference index (which honours the
        // project dependency graph, including transitive project references) to visit just those
        // documents instead of building a semantic model for every document in the solution.
        var references = await SymbolFinder.FindReferencesAsync(updatedConcreteType, updatedSolution, ct).ConfigureAwait(false);
        var referencingDocumentIds = references
            .SelectMany(reference => reference.Locations)
            .Select(location => location.Document.Id)
            .Distinct();

        foreach (var documentId in referencingDocumentIds)
        {
            var document = updatedSolution.GetDocument(documentId);
            if (document is null)
            {
                continue;
            }

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
                (original, _) =>
                {
                    // FORMAT-BUG-002: Preserve the original type node's surrounding trivia.
                    // `ParseTypeName` produces a bare identifier with no trivia; without
                    // `WithTriviaFrom` the trailing space between type and parameter name is
                    // lost, producing `IAnimalServiceservice` instead of `IAnimalService service`.
                    var replacementType = SyntaxFactory.ParseTypeName(resolvedInterfaceName)
                        .WithTriviaFrom(original.Type!);
                    return original.WithType(replacementType);
                });
            updatedSolution = updatedSolution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = $"Extract interface '{resolvedInterfaceName}' from '{typeName}' and update constructor dependencies";
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);
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
            if (doc.FilePath is null || !FileSystemPath.IsStrictDescendant(projectDirectory, doc.FilePath))
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

    /// <summary>
    /// Renders <paramref name="path"/> relative to the project directory so public refusal messages
    /// never disclose absolute filesystem paths.
    /// </summary>
    private static string DescribeProjectRelativePath(string projectDirectory, string path) =>
        Path.GetRelativePath(projectDirectory, path).Replace('\\', '/');

    private static Project ResolveProject(Solution solution, string targetProjectName)
    {
        return solution.Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, targetProjectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, targetProjectName, StringComparison.OrdinalIgnoreCase))
               ?? throw WorkspaceProjectResolver.NotFound(solution.Projects.Select(project => project.Name));
    }

    private static TypeDeclarationSyntax FindTypeDeclaration(CompilationUnitSyntax root, string typeName)
    {
        return root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, typeName, StringComparison.Ordinal))
            ?? throw new PublicInvalidOperationException($"Type '{typeName}' was not found in the source document.");
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

    /// <summary>
    /// FLAG-10C: Derive a namespace that matches the file's on-disk location relative to the
    /// project root. If the interface is being placed in a subdirectory like <c>Interfaces/</c>,
    /// the returned namespace becomes <c>{base}.Interfaces</c> so the new file is consistent with
    /// the conventional sibling files in the same folder.
    /// </summary>
    private static string DeriveTargetNamespaceForPath(Project targetProject, string projectDirectory, string interfaceDirectory)
    {
        var baseNamespace = DeriveTargetNamespace(targetProject);
        if (string.IsNullOrWhiteSpace(projectDirectory) ||
            string.IsNullOrWhiteSpace(interfaceDirectory) ||
            string.Equals(projectDirectory, interfaceDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return baseNamespace;
        }

        var relative = Path.GetRelativePath(projectDirectory, interfaceDirectory);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return baseNamespace;
        }

        var segments = relative
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.Equals(segment, ".", StringComparison.Ordinal))
            .Select(segment => segment.Replace(' ', '_'))
            .ToArray();

        return segments.Length == 0 ? baseNamespace : $"{baseNamespace}.{string.Join('.', segments)}";
    }

    private static async Task DetectExistingTypeConflictAsync(
        string workspaceId, ICompilationCache compilationCache,
        Solution solution, string namespaceName, string typeName, CancellationToken ct)
    {
        var fullyQualifiedName = string.IsNullOrWhiteSpace(namespaceName)
            ? typeName
            : $"{namespaceName}.{typeName}";

        foreach (var project in solution.Projects)
        {
            var compilation = await compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation?.GetTypeByMetadataName(fullyQualifiedName) is not null)
            {
                throw new PublicInvalidOperationException(
                    $"Type '{typeName}' already exists in project '{project.Name}' " +
                    $"(namespace '{namespaceName}'). Choose a different interface name to avoid conflicts.");
            }
        }
    }

    private static CompilationUnitSyntax CreateInterfaceCompilationUnit(
        CompilationUnitSyntax sourceRoot,
        INamedTypeSymbol typeSymbol,
        string interfaceName,
        string namespaceName,
        bool isCrossProject,
        CSharpParseOptions receivingOptions,
        InterfaceMemberProjector projector)
    {
        var interfaceMembers = typeSymbol.GetMembers().Where(InterfaceMemberProjector.IsProjectable).Select(projector.Project).ToList();

        if (interfaceMembers.Count == 0)
        {
            throw new PublicInvalidOperationException($"Type '{typeSymbol.Name}' does not have public instance members that can be extracted.");
        }

        // BUG-003: Match the source type's accessibility. A cross-project extraction MUST stay
        // public because the interface is moved into a different assembly and an internal
        // interface would not be visible to consumers; for in-project extraction we honor
        // internal/private types so we don't accidentally widen visibility.
        var matchSourceAccessibility = !isCrossProject;
        var accessibilityToken = matchSourceAccessibility
            ? GetAccessibilityToken(typeSymbol.DeclaredAccessibility)
            : SyntaxFactory.Token(SyntaxKind.PublicKeyword);

        var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(interfaceName)
            .AddModifiers(accessibilityToken)
            .WithMembers(SyntaxFactory.List(interfaceMembers));

        var usings = UsingDirectiveSynthesizer.BuildUsingDirectives(
            sourceRoot.Usings, projector.RequiredNamespaces(string.IsNullOrWhiteSpace(namespaceName) ? null : namespaceName));
        var cleanUsings = SyntaxFactory.List(usings.Select(u => u.WithoutTrivia()));
        var destinationNamespace = ReceivingNamespaceFactory.Create(namespaceName, receivingOptions);
        MemberDeclarationSyntax wrappedMember = destinationNamespace is null
            ? interfaceDeclaration
            : destinationNamespace.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDeclaration));
        // Synthesized declarations have no original body layout to preserve.
        return SyntaxFactory.CompilationUnit().WithUsings(cleanUsings)
            .WithMembers(SyntaxFactory.SingletonList(wrappedMember)).NormalizeWhitespace();
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

    private static Solution EnsureProjectReference(Solution solution, ProjectId sourceProjectId, ProjectId targetProjectId)
    {
        var sourceProject = solution.GetProject(sourceProjectId)
            ?? throw new InvalidOperationException("Source project was not found in the updated solution.");

        if (sourceProject.ProjectReferences.Any(reference => reference.ProjectId == targetProjectId))
        {
            return solution;
        }

        var targetProject = solution.GetProject(targetProjectId)
            ?? throw new InvalidOperationException("Target project was not found in the updated solution.");

        if (ProjectGraphHelpers.WouldCreateProjectReferenceCycle(sourceProject, targetProject))
        {
            throw new PublicInvalidOperationException(
                $"Adding project reference from '{sourceProject.Name}' to '{targetProject.Name}' would create a circular dependency.");
        }

        return solution.AddProjectReference(sourceProjectId, new ProjectReference(targetProjectId));
    }

    private static async Task<Solution> CreateInterfaceExtractionSolutionAsync(
        string workspaceId,
        ICompilationCache compilationCache,
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

        await DetectExistingTypeConflictAsync(
            workspaceId, compilationCache, solution, namespaceName, interfaceName, ct).ConfigureAwait(false);

        // gh #765 — project members and collect required namespaces semantically (see
        // PreviewExtractInterfaceAsync). The DI-inversion path routes through here too.
        var nullableProjection = new InterfaceNullableProjection();
        var projector = await InterfaceMemberProjector.CreateAsync(sourceDocument, nullableProjection, ct).ConfigureAwait(false);
        var interfaceRoot = CreateInterfaceCompilationUnit(sourceRoot, typeSymbol, interfaceName, namespaceName, isCrossProject,
            ReceivingNamespaceFactory.RequireCSharpOptions(targetProject.ParseOptions), projector);
        var interfaceFileDirectory = isCrossProject
            ? ResolvePreferredInterfaceSubdirectory(solution, targetProject, targetProjectDirectory)
            : targetProjectDirectory;
        var interfaceFilePath = Path.Combine(interfaceFileDirectory, interfaceName + ".cs");
        if (File.Exists(interfaceFilePath))
        {
            throw new PublicInvalidOperationException($"Target interface file already exists: {DescribeProjectRelativePath(targetProjectDirectory, interfaceFilePath)}");
        }

        var updatedTypeDeclaration = InterfaceBaseTypeWriter.AddBaseType(typeDeclaration, interfaceName, multilineContinuation: true);
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDeclaration, updatedTypeDeclaration);
        // FORMAT-BUG-002 (dr-9-3-format-bug-002-destroys-source-formatting): do NOT
        // NormalizeWhitespace() the entire source compilation unit — that re-flows every node
        // in the file (collapses multi-line bodies, strips intentional spacing, reshuffles
        // indentation). `InterfaceBaseTypeWriter.AddBaseType` already emits the targeted
        // `: IName\n{` edit with correct trivia; ReplaceNode preserves surrounding source layout.
        // Mirrors the same decision made in PreviewExtractInterfaceAsync.

        // Item #1 — pass folders so MSBuildWorkspace resolves the disk path consistently.
        var diInterfaceFolders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, interfaceFilePath);
        var interfaceDocumentId = DocumentId.CreateNewId(targetProject.Id);
        var updatedSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot)
            .AddDocument(
                interfaceDocumentId,
                interfaceName + ".cs",
                SourceText.From(interfaceRoot.ToFullString()),
                folders: diInterfaceFolders,
                filePath: interfaceFilePath);

        if (isCrossProject)
        {
            updatedSolution = EnsureProjectReference(updatedSolution, sourceDocument.Project.Id, targetProject.Id);
        }

        return await nullableProjection.ApplyAsync(updatedSolution.GetDocument(interfaceDocumentId)
            ?? throw InvalidOperationErrors.Internal("Generated interface document is unavailable."), interfaceRoot, ct).ConfigureAwait(false);
    }

    private static string GetMetadataName(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? typeSymbol.MetadataName
            : typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName;
    }

}
