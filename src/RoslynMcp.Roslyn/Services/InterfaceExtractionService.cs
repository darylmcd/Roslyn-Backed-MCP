using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
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
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<InterfaceExtractionService> _logger;

    public InterfaceExtractionService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ICompilationCache compilationCache,
        ILogger<InterfaceExtractionService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _compilationCache = compilationCache;
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
        // Item #3 — severity-high-fail-produces-code-that-does-not-compi.
        // The legacy FilterRelevantUsings text-grepped the synthesized interface
        // for the FULL namespace name, but BuildInterfaceMembers renders types with
        // MinimallyQualifiedFormat (short names like NetworkInventory) — so the text
        // never contained "NetworkDocumentation.Core.Models" and the using was dropped,
        // producing post-apply CS0246/CS0535 (NetworkDocumentation audit §9.1). Fix:
        // walk the member symbols semantically to collect every referenced type's
        // containing namespace, and use that set (plus aliases/static usings from the
        // source file) as the authoritative using list.
        var requiredNamespaces = CollectReferencedNamespaces(candidateMembers, typeSymbol.ContainingNamespace);

        // Compute alreadyImplements BEFORE the conflict check so we can short-circuit
        // when the target type already implements a covering interface of the same name.
        // Bug fix (extract-interface-preview-duplicate-interface-when-already-implements):
        // previously the document-add path and the base-list rewrite were gated
        // independently — the document-add was unconditional, producing a duplicate
        // IFoo.cs even when the type's base list already contained IFoo (from a
        // sibling namespace). When alreadyImplements==true, return a no-op preview
        // with a warning so callers see why no changes were produced.
        var alreadyImplements = typeSymbol.AllInterfaces.Any(i =>
            string.Equals(i.Name, interfaceName, StringComparison.Ordinal));

        // Short-circuit ONLY when alreadyImplements==true. Otherwise still run the
        // conflict check so a same-name-but-different-namespace collision is reported
        // before we synthesize a new file.
        if (!alreadyImplements)
        {
            await ValidateNoConflictsAsync(workspaceId, solution, typeSymbol, interfaceName, ct).ConfigureAwait(false);
        }

        // BUG-003: Match the interface accessibility to the source type so an internal class
        // does not produce a public interface that escapes its assembly boundary.
        var interfaceAccessibilityToken = GetAccessibilityToken(typeSymbol.DeclaredAccessibility);
        var interfaceDecl = SyntaxFactory.InterfaceDeclaration(interfaceName)
            .WithModifiers(SyntaxFactory.TokenList(interfaceAccessibilityToken))
            .WithMembers(SyntaxFactory.List(interfaceMembers));

        var interfaceFileRoot = BuildInterfaceFile(interfaceDecl, typeDecl, sourceRoot, requiredNamespaces);

        TypeDeclarationSyntax updatedTypeDecl;
        if (alreadyImplements)
        {
            updatedTypeDecl = typeDecl;
        }
        else
        {
            // Use formatting-preserving syntax annotations so the existing source layout
            // (modifiers, expression bodies, format specifiers) is not disturbed
            // by a NormalizeWhitespace() pass over the entire compilation unit (BUG-004).
            var interfaceTypeSyntax = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName))
                .WithLeadingTrivia(SyntaxFactory.Space);
            if (typeDecl.BaseList is not null)
            {
                // dr-9-6-emits-continuation-on-a-new-line-instead-of-inli: when the source class is
                // `class Foo : IBar\n{` the last existing base type (`IBar`) carries a trailing
                // end-of-line trivia (the newline that originally preceded the `{`). Roslyn's
                // SeparatedSyntaxList.Add inserts a zero-trivia comma token AFTER that trailing
                // newline, producing `: IBar\n, INewInterface{`. To keep the continuation inline
                // (`: IBar, INewInterface`), transfer the last existing base type's trailing trivia
                // onto the new interface's trailing position and clear it from the original last
                // base type so the comma stays glued to it.
                var baseList = typeDecl.BaseList;
                var lastIndex = baseList.Types.Count - 1;
                BaseTypeSyntax newInterfaceType = interfaceTypeSyntax;
                if (lastIndex >= 0)
                {
                    var lastExisting = baseList.Types[lastIndex];
                    var trailing = lastExisting.GetTrailingTrivia();
                    if (trailing.Count > 0)
                    {
                        newInterfaceType = interfaceTypeSyntax.WithTrailingTrivia(trailing);
                        var strippedLast = lastExisting.WithTrailingTrivia(SyntaxTriviaList.Empty);
                        baseList = baseList.WithTypes(baseList.Types.Replace(lastExisting, strippedLast));
                    }
                }

                var newBaseList = baseList.AddTypes(newInterfaceType);
                updatedTypeDecl = typeDecl.WithBaseList(newBaseList);
            }
            else
            {
                // No existing base list. Attach `: IName` to the type declaration directly; Roslyn
                // emits the leading colon token automatically.
                var newBaseList = SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceTypeSyntax))
                    .WithLeadingTrivia(SyntaxFactory.Space);
                updatedTypeDecl = typeDecl.WithBaseList(newBaseList);
            }
        }

        // BUG-N12: avoid `: IName{` glued to the opening brace — keep `{` on its own line for readability.
        if (!alreadyImplements)
        {
            updatedTypeDecl = EnsureOpeningBraceOnOwnLine(updatedTypeDecl);
        }

        // BUG-004: Do NOT call NormalizeWhitespace() here — that re-flows the entire file and
        // produces unrelated formatting churn (collapses multi-line bodies, alters string format
        // specifier spacing). ReplaceNode preserves the surrounding source layout.
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDecl, updatedTypeDecl);

        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        var warnings = new List<string>();
        if (!alreadyImplements)
        {
            // Add interface document. Item #1 — pass folders so MSBuildWorkspace's
            // TryApplyChanges resolves the disk path consistently with our explicit write.
            var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
            var interfaceFilePath = Path.Combine(sourceDir, $"{interfaceName}.cs");
            var targetProject = newSolution.GetProject(sourceDocument.Project.Id)!;
            var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, interfaceFilePath);
            var interfaceDoc = targetProject.AddDocument($"{interfaceName}.cs", interfaceFileRoot.ToFullString(), folders: folders, filePath: interfaceFilePath);
            newSolution = interfaceDoc.Project.Solution;

            if (replaceUsages)
            {
                newSolution = await ReplaceConcreteUsagesAsync(
                    newSolution, sourceDocument.Project.Id, typeSymbol, interfaceName, ct).ConfigureAwait(false);
            }
        }
        else
        {
            // No-op preview: the type already implements an interface of this name.
            // Surface a warning so callers see why no changes were produced. The base-list
            // rewrite, document-add, and usage-replacement paths are all skipped — the
            // computed newSolution is byte-identical to the input solution.
            warnings.Add(
                $"Type '{typeName}' already implements an interface named '{interfaceName}'. " +
                $"No file changes were produced. " +
                $"If you intended to extract a cross-project interface contract, use extract_interface_cross_project_preview instead.");
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract interface '{interfaceName}' from '{typeName}' with {candidateMembers.Count} member(s)";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);

        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
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
        InterfaceDeclarationSyntax interfaceDecl,
        TypeDeclarationSyntax typeDecl,
        CompilationUnitSyntax sourceRoot,
        IReadOnlyCollection<string> requiredNamespaces)
    {
        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var usings = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceRoot.Usings, requiredNamespaces);

        if (namespaceDecl is FileScopedNamespaceDeclarationSyntax fileScopedNs)
        {
            var newNs = SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
            return SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }

        if (namespaceDecl is NamespaceDeclarationSyntax blockNs)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
            return SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }

        return SyntaxFactory.CompilationUnit()
            .WithUsings(usings)
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl))
            .NormalizeWhitespace();
    }

    /// <summary>
    /// Item #3 — semantic using-collection. Walk every candidate member symbol and collect
    /// the containing namespaces of every referenced type (parameter types, return types,
    /// property types, event types, type-parameter constraints, recursive type arguments on
    /// generics). The result set is the authoritative list of namespaces the generated
    /// interface file needs, independent of what the source file happened to declare.
    /// Excludes the interface's own containing namespace (self-reference is invalid as a using).
    /// </summary>
    private static IReadOnlyCollection<string> CollectReferencedNamespaces(
        IReadOnlyList<ISymbol> members,
        INamespaceSymbol? containingNamespace)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var ownNamespace = containingNamespace?.IsGlobalNamespace == false
            ? containingNamespace.ToDisplayString()
            : null;

        foreach (var member in members)
        {
            switch (member)
            {
                case IMethodSymbol method:
                    TypeNamespaceWalker.Collect(namespaces, method.ReturnType, ownNamespace);
                    foreach (var parameter in method.Parameters)
                    {
                        TypeNamespaceWalker.Collect(namespaces, parameter.Type, ownNamespace);
                    }
                    foreach (var typeParameter in method.TypeParameters)
                    {
                        foreach (var constraint in typeParameter.ConstraintTypes)
                        {
                            TypeNamespaceWalker.Collect(namespaces, constraint, ownNamespace);
                        }
                    }
                    break;
                case IPropertySymbol property:
                    TypeNamespaceWalker.Collect(namespaces, property.Type, ownNamespace);
                    break;
                case IEventSymbol evt:
                    TypeNamespaceWalker.Collect(namespaces, evt.Type, ownNamespace);
                    break;
            }
        }

        return namespaces;
    }

    private static TypeDeclarationSyntax EnsureOpeningBraceOnOwnLine(TypeDeclarationSyntax typeDecl)
    {
        var brace = typeDecl.OpenBraceToken;
        if (brace.IsMissing)
            return typeDecl;

        if (brace.LeadingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia)))
            return typeDecl;

        return typeDecl.WithOpenBraceToken(
            brace.WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine))));
    }

    /// <remarks>
    /// Reads compilations through the shared <see cref="ICompilationCache"/>: the only call site
    /// passes the LIVE <see cref="IWorkspaceManager.GetCurrentSolution"/> snapshot taken before any
    /// in-memory fork, so the version-keyed cache is a valid source. Mirrors
    /// <c>CrossProjectRefactoringService.DetectExistingTypeConflictAsync</c>.
    /// </remarks>
    private async Task ValidateNoConflictsAsync(
        string workspaceId, Solution solution, INamedTypeSymbol typeSymbol, string interfaceName, CancellationToken ct)
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
                var comp = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
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

    /// <remarks>
    /// Deliberately NOT routed through <see cref="ICompilationCache"/>. The caller hands in a
    /// FORKED solution (built with <c>WithDocumentSyntaxRoot</c> + <c>AddDocument</c> and not yet
    /// applied to the workspace), so it carries the same workspace version as the live solution.
    /// A version-keyed cache would either hand back the pre-fork compilation — which lacks the
    /// synthesized interface document — or pollute the shared cache with an in-memory-only fork.
    /// The raw <c>project.GetCompilationAsync</c> below is the correct read here.
    /// </remarks>
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

    /// <summary>
    /// Maps a Roslyn <see cref="Accessibility"/> level to the matching C# modifier token used on
    /// generated interface declarations. Internal types map to <c>internal</c>; the rest fall back
    /// to <c>public</c> since interfaces extracted from a private nested class can still be public
    /// within the containing type.
    /// </summary>
    private static SyntaxToken GetAccessibilityToken(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Internal => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        Accessibility.ProtectedAndInternal => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        Accessibility.ProtectedOrInternal => SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        _ => SyntaxFactory.Token(SyntaxKind.PublicKeyword),
    };
}
