using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

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
    /// Test seam: the post-generation compilation read by <c>replaceUsages</c>. Production always reads the forked
    /// project's own compilation; a test substitutes an unavailable or unrelated one to prove the refusals.
    /// </summary>
    internal Func<Project, CancellationToken, Task<Compilation?>> ReplacementCompilationAsync { get; set; } =
        static (project, ct) => project.GetCompilationAsync(ct);

    /// <summary>
    /// Previews extracting an interface from the specified type, optionally replacing usages of
    /// the concrete type with the new interface in declaration positions (parameters, fields, locals, return types, generic arguments).
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(
        string workspaceId, string filePath, string typeName, string interfaceName,
        IReadOnlyList<string>? memberNames, bool replaceUsages, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var (sourceDocument, sourceRoot, typeDecl, typeSymbol) =
            await ResolveTypeAsync(solution, filePath, typeName, ct).ConfigureAwait(false);

        var candidateMembers = SelectCandidateMembers(typeSymbol, memberNames, typeName);
        var nullableProjection = new InterfaceNullableProjection();
        var projector = await InterfaceMemberProjector.CreateAsync(sourceDocument, nullableProjection, sourceDocument.Project, ct).ConfigureAwait(false);
        var interfaceMembers = candidateMembers.Select(projector.Project).ToList();
        // Item #3 — severity-high-fail-produces-code-that-does-not-compi.
        // Text-grepping the synthesized interface for namespace names dropped usings whose last
        // segment did not match the rendered short name (NetworkDocumentation audit §9.1). The
        // projector records every type each projected member refers to, so the interface file's
        // using list comes from those symbols (plus aliases/static usings from the source file).
        var requiredNamespaces = projector.RequiredNamespaces(
            typeSymbol.ContainingNamespace.IsGlobalNamespace ? null : typeSymbol.ContainingNamespace.ToDisplayString());

        // Decide alreadyImplements BEFORE the conflict check so a type that already implements an interface of
        // this name which covers every requested member short-circuits to a no-op preview with a warning
        // (extract-interface-preview-duplicate-interface-when-already-implements). A same-named interface that
        // does NOT cover the requested members is a different contract: the requested interface is generated
        // beside it and every reference the new interface would capture is re-pinned (SiblingInterfaceRepin).
        var sameNamed = ImplementedInterfaceContract.FindNamed(typeSymbol, interfaceName);
        var alreadyImplements = ImplementedInterfaceContract.FindCovering(typeSymbol, sameNamed, candidateMembers) is not null;

        // Short-circuit ONLY when alreadyImplements==true. Otherwise run the conflict check so a collision with
        // a type in the target namespace (including an uncovering same-named interface there) is reported
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

        var destinationGlobalPlainNamespaces = await UsingDirectiveSynthesizer
            .GetProjectGlobalPlainNamespacesAsync(sourceDocument.Project, ct).ConfigureAwait(false);
        var sourceSemanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Source semantic model is unavailable.");
        var interfaceFileRoot = BuildInterfaceFile(
            interfaceDecl, typeDecl, sourceRoot, requiredNamespaces,
            projector.ReferencedUsingAliases, projector.ReferencedUnqualifiedTypes, destinationGlobalPlainNamespaces,
            sourceSemanticModel, ct);

        var newSolution = solution;

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
            newSolution = await nullableProjection.ApplyAsync(
                interfaceDoc, interfaceFileRoot, ReceivingNamespaceFactory.RequireCSharpOptions(sourceDocument.Project.ParseOptions), ct).ConfigureAwait(false);

            // Pin every reference the new interface would capture (the implemented sibling's and any unrelated
            // same-named type's) to what it bound to before, then spell the base list against the generated symbol.
            // BUG-004: Do NOT call NormalizeWhitespace() here — that re-flows the entire file and
            // produces unrelated formatting churn. The base list is a targeted node replacement,
            // spelled after the generated symbol exists so it binds to it.
            var repin = await SiblingInterfaceRepin.RepinAsync(
                newSolution, interfaceDoc.Id, sourceDocument.Id, typeDecl.Span, ct).ConfigureAwait(false);
            newSolution = await GeneratedInterfaceReference.AddToBaseListAsync(
                repin.Solution, sourceDocument.Id, repin.TypeMark, interfaceDoc.Id, multilineContinuation: false, ct).ConfigureAwait(false);

            if (replaceUsages)
            {
                newSolution = await ReplaceConcreteUsagesAsync(
                    newSolution, sourceDocument.Project.Id, typeSymbol, interfaceDoc.Id, ct).ConfigureAwait(false);
            }

            // The source type's file may be linked into other projects: copy its final text to those copies, so the pending
            // solution matches what the apply writes to disk and the gate below sees every project that compiles that file.
            newSolution = await InterfaceLinkedDocumentReconciler.ReconcileLinkedCopiesAsync(
                solution, newSolution, sourceDocument.Id, sourceDocument.Project.Id, ct).ConfigureAwait(false);

            // Retyping usages can break a consumer (a member the interface lacks, generic variance, an override), and
            // re-pinning edits documents the caller did not name: refuse either preview rather than publish a token
            // whose apply does not compile. The remedy names every cause that could apply to this request.
            var sameNamedType = sameNamed.Count > 0 || repin.Repinned;
            if (replaceUsages || sameNamedType)
            {
                await InterfacePreviewCompilationGate.ValidateAsync(
                    solution, newSolution, InterfacePreviewCompilationGate.RemedyFor(sameNamedType, replaceUsages), ct).ConfigureAwait(false);
            }
        }
        else
        {
            // No-op preview: the type already implements an interface of this name that covers every requested member.
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
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            PreviewKind.ExtractInterface);

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
            .Where(InterfaceMemberProjector.IsProjectable)
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

    private static CompilationUnitSyntax BuildInterfaceFile(
        InterfaceDeclarationSyntax interfaceDecl,
        TypeDeclarationSyntax typeDecl,
        CompilationUnitSyntax sourceRoot,
        IReadOnlyCollection<string> requiredNamespaces,
        IReadOnlyCollection<string> referencedUsingAliases,
        IReadOnlyCollection<ITypeSymbol> referencedUnqualifiedTypes,
        IReadOnlyCollection<string> destinationGlobalPlainNamespaces,
        SemanticModel sourceSemanticModel,
        CancellationToken ct)
    {
        var namespaceScopes = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().ToArray();
        var (sourceUsings, qualifiedNamespaceUsings) = UsingDirectiveSynthesizer.SelectVisibleAliasUsings(
            sourceRoot, typeDecl, referencedUsingAliases, referencedUnqualifiedTypes, sourceSemanticModel, ct);
        var usings = UsingDirectiveSynthesizer.BuildUsingDirectives(
            sourceUsings, requiredNamespaces, preserveGlobalUsings: false,
            destinationGlobalPlainNamespaces: destinationGlobalPlainNamespaces,
            sourceSemanticModel: sourceSemanticModel, ct: ct);
        var emittedUsings = SyntaxFactory.List(usings.Concat(qualifiedNamespaceUsings));
        var externs = UsingDirectiveSynthesizer.BuildRequiredExternAliases(
            sourceRoot, typeDecl, interfaceDecl.Members, emittedUsings);
        var root = SyntaxFactory.CompilationUnit().WithExterns(externs).WithUsings(emittedUsings);

        if (namespaceScopes.Length == 0)
        {
            return root.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl))
                .NormalizeWhitespace();
        }

        var namespaceName = SyntaxFactory.ParseName(string.Join(".", namespaceScopes.Select(scope => scope.Name.WithoutTrivia().ToString())));
        MemberDeclarationSyntax namespaceDecl = namespaceScopes[0] is FileScopedNamespaceDeclarationSyntax
            ? SyntaxFactory.FileScopedNamespaceDeclaration(namespaceName)
            : SyntaxFactory.NamespaceDeclaration(namespaceName);
        namespaceDecl = ((BaseNamespaceDeclarationSyntax)namespaceDecl)
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
        return root.WithMembers(SyntaxFactory.SingletonList(namespaceDecl))
            .NormalizeWhitespace();
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
            Compilation compilation;
            try
            {
                compilation = await InterfaceConflictCompilation.RequireAsync(
                    workspaceId, _compilationCache, project, ct).ConfigureAwait(false);
            }
            catch (PublicInvalidOperationException)
            {
                _logger.LogWarning("Interface conflict check could not obtain compilation for project '{ProjectName}'", project.Name);
                throw;
            }
            if (compilation.GetTypeByMetadataName(fullyQualifiedInterfaceName) is not null)
            {
                throw new InvalidOperationException(
                    $"Type '{interfaceName}' already exists in project '{project.Name}' " +
                    $"(namespace '{namespaceName}'). Choose a different interface name to avoid conflicts.");
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
    private async Task<Solution> ReplaceConcreteUsagesAsync(
        Solution solution, ProjectId projectId, INamedTypeSymbol typeSymbol,
        DocumentId interfaceDocumentId, CancellationToken ct)
    {
        // The caller already forked the generated document and base list into `solution`. Returning it unchanged when a
        // requested replacement cannot run would publish a preview that silently lacks it, so each gap refuses instead.
        var project = solution.GetProject(projectId)!;
        var compilation = await ReplacementCompilationAsync(project, ct).ConfigureAwait(false)
            ?? throw new PublicInvalidOperationException(
                $"replaceUsages could not compile project '{project.Name}' after generating the interface, so its usages cannot be rewritten; " +
                "no preview was produced. Retry after reloading the workspace, or preview with replaceUsages=false.");

        // A symbol-identity lookup, not `GetTypeByMetadataName(ToDisplayString())`: the display string escapes keyword
        // namespace segments (`@namespace.Type`) and nested/generic names, which no metadata lookup finds.
        var updatedTypeSymbol = SymbolFinder.FindSimilarSymbols(typeSymbol, compilation, ct).FirstOrDefault()
            ?? throw new PublicInvalidOperationException(
                $"replaceUsages could not find type '{typeSymbol.Name}' in the generated solution, so its usages cannot be rewritten; " +
                "no preview was produced. Preview with replaceUsages=false.");

        var generated = await GeneratedInterfaceReference.ResolveGeneratedAsync(solution, interfaceDocumentId, ct).ConfigureAwait(false);
        var refs = await SymbolFinder.FindReferencesAsync(updatedTypeSymbol, solution, ct).ConfigureAwait(false);

        // One rewrite per document: every location is a span of the same document snapshot, so applying
        // them together keeps each edit (re-reading the document per location would drop all but the last).
        foreach (var documentLocations in refs.SelectMany(r => r.Locations).GroupBy(l => l.Document.Id))
        {
            // A source-generated document is not editable: the generator owns its text, so its usages stay as emitted.
            var document = ReferenceDocumentResolver.ResolveEditable(solution, documentLocations.First().Document);
            if (document is null) continue;

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal($"Syntax for reference document '{document.Name}' is unavailable.");
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
                ?? throw InvalidOperationErrors.Internal($"Semantic model for reference document '{document.Name}' is unavailable.");

            // Every spelling that types a declaration (bare, qualified, aliased, return type, local, generic argument, ...)
            // is retyped; positions that are not usages of the type (construction, static access, typeof, casts) stay concrete.
            var targets = ConcreteUsageRewriter.FindReplaceableSpellings(root, documentLocations.Select(l => l.Location.SourceSpan));
            if (targets.Count == 0) continue;

            var updatedRoot = root.ReplaceNodes(
                targets,
                (original, _) => GeneratedInterfaceReference.Create(generated, model, original.SpanStart).WithTriviaFrom(original));
            solution = solution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
        }

        return solution;
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
