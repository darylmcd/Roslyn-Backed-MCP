using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Extracts interfaces from concrete types, generating a new interface file with selected member
/// signatures and optionally replacing concrete type references with the interface across the solution.
/// </summary>
public sealed class InterfaceExtractionService : IInterfaceExtractionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<InterfaceExtractionService> _logger;

    public InterfaceExtractionService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<InterfaceExtractionService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    /// <summary>
    /// Previews extracting an interface from the specified type, optionally replacing usages of
    /// the concrete type with the new interface in parameter and field declarations.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(
        string workspaceId, string filePath, string typeName, string interfaceName,
        IReadOnlyList<string>? memberNames, bool replaceUsages, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var (sourceDocument, sourceRoot, typeDecl, typeSymbol) =
            await ResolveTypeAsync(solution, filePath, typeName, ct).ConfigureAwait(false);

        var candidateMembers = SelectCandidateMembers(typeSymbol, memberNames, typeName);
        var interfaceMembers = BuildInterfaceMembers(candidateMembers);

        await ValidateNoConflictsAsync(solution, typeSymbol, interfaceName, ct).ConfigureAwait(false);

        var interfaceDecl = SyntaxFactory.InterfaceDeclaration(interfaceName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List(interfaceMembers));

        var interfaceFileRoot = BuildInterfaceFile(interfaceDecl, typeDecl, sourceRoot);

        // Add interface to type's base list
        var interfaceTypeSyntax = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName));
        TypeDeclarationSyntax updatedTypeDecl;
        if (typeDecl.BaseList is not null)
        {
            var newBaseList = typeDecl.BaseList.AddTypes(interfaceTypeSyntax);
            updatedTypeDecl = typeDecl.WithBaseList(newBaseList);
        }
        else
        {
            updatedTypeDecl = typeDecl.WithBaseList(
                SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceTypeSyntax)));
        }

        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDecl, updatedTypeDecl);
        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add interface document
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
        var interfaceFilePath = Path.Combine(sourceDir, $"{interfaceName}.cs");
        var interfaceDoc = newSolution.GetProject(sourceDocument.Project.Id)!
            .AddDocument($"{interfaceName}.cs", interfaceFileRoot.ToFullString(), filePath: interfaceFilePath);
        newSolution = interfaceDoc.Project.Solution;

        if (replaceUsages)
        {
            newSolution = await ReplaceConcreteUsagesAsync(
                newSolution, sourceDocument.Project.Id, typeSymbol, interfaceName, ct).ConfigureAwait(false);
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract interface '{interfaceName}' from '{typeName}' with {candidateMembers.Count} member(s)";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static async Task<(Document SourceDocument, CompilationUnitSyntax SourceRoot, TypeDeclarationSyntax TypeDecl, INamedTypeSymbol TypeSymbol)>
        ResolveTypeAsync(Solution solution, string filePath, string typeName, CancellationToken ct)
    {
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created.");

        var typeDecl = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' not found in {filePath}.");

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve type '{typeName}'.");

        return (sourceDocument, sourceRoot, typeDecl, typeSymbol);
    }

    private static List<ISymbol> SelectCandidateMembers(
        INamedTypeSymbol typeSymbol, IReadOnlyList<string>? memberNames, string typeName)
    {
        var candidateMembers = typeSymbol.GetMembers()
            .Where(m => !m.IsStatic && !m.IsImplicitlyDeclared &&
                        m.DeclaredAccessibility == Accessibility.Public &&
                        m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol)
            .ToList();

        if (memberNames is not null && memberNames.Count > 0)
        {
            var nameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
            candidateMembers = candidateMembers.Where(m => nameSet.Contains(m.Name)).ToList();
            if (candidateMembers.Count == 0)
                throw new InvalidOperationException("None of the specified member names matched public members of the type.");
        }

        if (candidateMembers.Count == 0)
            throw new InvalidOperationException($"Type '{typeName}' has no public instance members to extract.");

        return candidateMembers;
    }

    private static List<MemberDeclarationSyntax> BuildInterfaceMembers(List<ISymbol> candidateMembers)
    {
        var interfaceMembers = new List<MemberDeclarationSyntax>();
        foreach (var member in candidateMembers)
        {
            switch (member)
            {
                case IMethodSymbol method:
                    var parameters = method.Parameters.Select(p =>
                        SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                            .WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))
                            .WithDefault(p.HasExplicitDefaultValue
                                ? SyntaxFactory.EqualsValueClause(SyntaxFactory.ParseExpression(GetDefaultValueText(p)))
                                : null));

                    var returnType = SyntaxFactory.ParseTypeName(method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    var methodDecl = SyntaxFactory.MethodDeclaration(returnType, method.Name)
                        .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

                    if (method.TypeParameters.Length > 0)
                    {
                        var typeParams = method.TypeParameters.Select(tp =>
                            SyntaxFactory.TypeParameter(tp.Name));
                        methodDecl = methodDecl.WithTypeParameterList(
                            SyntaxFactory.TypeParameterList(SyntaxFactory.SeparatedList(typeParams)));
                    }

                    interfaceMembers.Add(methodDecl);
                    break;

                case IPropertySymbol property:
                    var propType = SyntaxFactory.ParseTypeName(property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    var accessors = new List<AccessorDeclarationSyntax>();
                    if (property.GetMethod is not null)
                        accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                    if (property.SetMethod is not null && !property.SetMethod.IsInitOnly)
                        accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

                    interfaceMembers.Add(SyntaxFactory.PropertyDeclaration(propType, property.Name)
                        .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors))));
                    break;

                case IEventSymbol evt:
                    var eventType = SyntaxFactory.ParseTypeName(evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    interfaceMembers.Add(SyntaxFactory.EventFieldDeclaration(
                        SyntaxFactory.VariableDeclaration(eventType)
                            .WithVariables(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(evt.Name))))
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                    break;
            }
        }
        return interfaceMembers;
    }

    private static CompilationUnitSyntax BuildInterfaceFile(
        InterfaceDeclarationSyntax interfaceDecl, TypeDeclarationSyntax typeDecl, CompilationUnitSyntax sourceRoot)
    {
        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        if (namespaceDecl is FileScopedNamespaceDeclarationSyntax fileScopedNs)
        {
            var newNs = SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
            return SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }

        if (namespaceDecl is NamespaceDeclarationSyntax blockNs)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
            return SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }

        return SyntaxFactory.CompilationUnit()
            .WithUsings(sourceRoot.Usings)
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl))
            .NormalizeWhitespace();
    }

    private async Task ValidateNoConflictsAsync(
        Solution solution, INamedTypeSymbol typeSymbol, string interfaceName, CancellationToken ct)
    {
        var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        var fullyQualifiedInterfaceName = string.IsNullOrWhiteSpace(namespaceName)
            ? interfaceName
            : $"{namespaceName}.{interfaceName}";

        foreach (var project in solution.Projects)
        {
            try
            {
                var comp = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (comp?.GetTypeByMetadataName(fullyQualifiedInterfaceName) is not null)
                {
                    throw new InvalidOperationException(
                        $"Type '{interfaceName}' already exists in project '{project.Name}' " +
                        $"(namespace '{namespaceName}'). Choose a different interface name to avoid conflicts.");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Skipping conflict check in project '{ProjectName}'", project.Name);
            }
        }
    }

    private static async Task<Solution> ReplaceConcreteUsagesAsync(
        Solution solution, ProjectId projectId, INamedTypeSymbol typeSymbol,
        string interfaceName, CancellationToken ct)
    {
        var compilation = await solution.GetProject(projectId)!
            .GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null) return solution;

        var updatedTypeSymbol = compilation.GetTypeByMetadataName(typeSymbol.ToDisplayString());
        if (updatedTypeSymbol is null) return solution;

        var refs = await SymbolFinder.FindReferencesAsync(updatedTypeSymbol, solution, ct).ConfigureAwait(false);
        foreach (var refSymbol in refs)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var refDoc = refLocation.Document;
                var refRoot = await refDoc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (refRoot is null) continue;

                var refNode = refRoot.FindNode(refLocation.Location.SourceSpan);
                var parent = refNode.Parent;

                bool shouldReplace = parent is ParameterSyntax ||
                    (parent is VariableDeclarationSyntax vd && vd.Parent is FieldDeclarationSyntax);

                if (shouldReplace && refNode is IdentifierNameSyntax identNode)
                {
                    var newIdentNode = SyntaxFactory.IdentifierName(interfaceName)
                        .WithTriviaFrom(identNode);
                    refRoot = refRoot.ReplaceNode(identNode, newIdentNode);
                    solution = solution.WithDocumentSyntaxRoot(refDoc.Id, refRoot);
                }
            }
        }

        return solution;
    }

    private static string GetDefaultValueText(IParameterSymbol parameter)
    {
        if (parameter.ExplicitDefaultValue is null)
            return "default";
        if (parameter.ExplicitDefaultValue is string s)
            return $"\"{s}\"";
        if (parameter.ExplicitDefaultValue is bool b)
            return b ? "true" : "false";
        return parameter.ExplicitDefaultValue.ToString() ?? "default";
    }
}
