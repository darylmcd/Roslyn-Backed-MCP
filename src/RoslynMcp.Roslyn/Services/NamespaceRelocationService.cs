using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Provides preview operations for relocating a type between namespaces inside the same
/// project. Rewrites the type's <c>namespace</c> declaration, optionally moves the file to a
/// new path inside the project, and adjusts consumer <c>using</c> directives respecting
/// ambient-namespace resolution (consumers whose own namespace equals or is a descendant of the
/// source or target namespace may need no <c>using</c> change at all).
/// </summary>
/// <remarks>
/// Pairs with <c>get_namespace_dependencies</c> to close circular namespace dependencies.
/// For example, moving <c>DevicePlatform</c> from <c>Child.Sub</c> to <c>Child</c> can break
/// a cycle where <c>Child</c> imports <c>Child.Sub</c> which in turn references <c>Child</c>.
/// </remarks>
public interface INamespaceRelocationService
{
    /// <summary>
    /// Previews relocating a top-level type between namespaces inside the same project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier returned by <c>workspace_load</c>.</param>
    /// <param name="typeName">Name of the top-level type to relocate (must be unique within <paramref name="fromNamespace"/>).</param>
    /// <param name="fromNamespace">Fully-qualified namespace currently containing the type.</param>
    /// <param name="toNamespace">Fully-qualified destination namespace inside the same project.</param>
    /// <param name="newFilePath">Optional absolute destination path. If null, the file stays in place and only its namespace declaration is rewritten.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewChangeTypeNamespaceAsync(
        string workspaceId,
        string typeName,
        string fromNamespace,
        string toNamespace,
        string? newFilePath,
        CancellationToken ct);
}

/// <inheritdoc cref="INamespaceRelocationService"/>
public sealed class NamespaceRelocationService : INamespaceRelocationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<NamespaceRelocationService> _logger;

    public NamespaceRelocationService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ILogger<NamespaceRelocationService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewChangeTypeNamespaceAsync(
        string workspaceId,
        string typeName,
        string fromNamespace,
        string toNamespace,
        string? newFilePath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new PublicArgumentException("Type name must be provided.", nameof(typeName));
        }
        if (string.IsNullOrWhiteSpace(fromNamespace))
        {
            throw new PublicArgumentException("Source namespace must be provided (use a file-move tool if the type currently has no namespace).", nameof(fromNamespace));
        }
        if (string.IsNullOrWhiteSpace(toNamespace))
        {
            throw new PublicArgumentException("Destination namespace must be provided.", nameof(toNamespace));
        }
        if (NamespaceNameIdentity.AreSame(fromNamespace, toNamespace))
        {
            throw new PublicArgumentException("Source and destination namespaces must differ.", nameof(toNamespace));
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Locate the type by (namespace + name). Must resolve uniquely across the project that
        // owns the source file. We search every document in every project for a TypeDeclaration
        // whose name matches AND whose enclosing namespace matches fromNamespace.
        var (sourceDocument, sourceRoot, typeDecl, sourceProject) =
            await FindTypeInNamespaceAsync(solution, typeName, fromNamespace, ct).ConfigureAwait(false);
        MovedDeclarationFileWriter.EnsureTopLevelDeclaration(typeDecl);

        // Compute destination file path. If the caller omitted newFilePath, the file stays put.
        var sourceFilePath = sourceDocument.FilePath
            ?? throw new InvalidOperationException("Source document must have a file path on disk.");
        var resolvedDestinationPath = ResolveDestinationPath(sourceFilePath, newFilePath, sourceProject);
        var isRelocating = !string.Equals(
            Path.GetFullPath(sourceFilePath),
            Path.GetFullPath(resolvedDestinationPath),
            StringComparison.OrdinalIgnoreCase);

        if (isRelocating &&
            solution.Projects.SelectMany(p => p.Documents)
                .Any(d => d.FilePath is not null &&
                          string.Equals(
                              Path.GetFullPath(d.FilePath),
                              Path.GetFullPath(resolvedDestinationPath),
                              StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Target file already exists: {resolvedDestinationPath}");
        }

        var warnings = new List<string>();

        // Rewrite the type's containing namespace, which may itself be nested:
        //   (a) without file relocation, when that declaration is top-level and the selected type
        //       is its only member, rename it in place;
        //   (b) without file relocation otherwise (other members: types, enums, delegates, nested
        //       namespaces; or a nested declaration), move only the selected type into a new
        //       top-level namespace declaration in the same file;
        //   (c) with file relocation, write the selected type to the destination file and
        //       remove its declaration from the source file.
        var (destinationDocument, movedTypeIdentifier, siblingTypesRemaining) = await ApplyNamespaceRewriteAsync(
            solution,
            sourceDocument,
            sourceRoot,
            typeDecl,
            fromNamespace,
            toNamespace,
            resolvedDestinationPath,
            isRelocating,
            ct).ConfigureAwait(false);
        // Rewrite consumer using directives respecting ambient-namespace resolution.
        var consumerUpdates = await UpdateConsumerUsingsAsync(
            destinationDocument.Project.Solution,
            typeName,
            fromNamespace,
            toNamespace,
            siblingTypesRemaining,
            ct).ConfigureAwait(false);
        var updatedSolution = consumerUpdates.Solution;
        await MovedTypeDestinationIdentity.EnsureAvailableAsync(sourceProject, destinationDocument, movedTypeIdentifier,
            updatedSolution, typeDecl, ct).ConfigureAwait(false);

        if (consumerUpdates.FallbackKeptOldUsingFiles.Count > 0)
        {
            warnings.Add(
                "Kept `using " + fromNamespace + ";` on " + consumerUpdates.FallbackKeptOldUsingFiles.Count +
                " consumer file(s) where other symbols still reference the old namespace. " +
                "Re-run `get_namespace_dependencies` after apply to confirm the cycle is closed.");
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var description = isRelocating
            ? $"Change namespace of '{typeName}' from '{fromNamespace}' to '{toNamespace}' (file -> {Path.GetFileName(resolvedDestinationPath)})"
            : $"Change namespace of '{typeName}' from '{fromNamespace}' to '{toNamespace}'";
        var token = _previewStore.Store(
            workspaceId,
            updatedSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes);

        _logger.LogInformation(
            "Prepared change-type-namespace preview for {TypeName} from {From} to {To} in workspace {WorkspaceId}",
            typeName,
            fromNamespace,
            toNamespace,
            workspaceId);

        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            warnings.Count > 0 ? warnings : null);
    }

    /// <summary>
    /// Locate the type declaration whose containing namespace exactly matches
    /// <paramref name="fromNamespace"/>. Throws if not found or if multiple candidates exist
    /// (the caller must disambiguate via namespace).
    /// </summary>
    private static async Task<(Document Document, CompilationUnitSyntax Root, TypeDeclarationSyntax TypeDecl, Project Project)>
        FindTypeInNamespaceAsync(Solution solution, string typeName, string fromNamespace, CancellationToken ct)
    {
        var matches = new List<(Document Document, CompilationUnitSyntax Root, TypeDeclarationSyntax TypeDecl, Project Project)>();
        var fromKey = NamespaceNameIdentity.Of(fromNamespace);
        // Kind of a declaration that matched name + namespace but is not a TypeDeclarationSyntax
        // (enum, delegate): reported by name instead of as "not found" when nothing else matched.
        SyntaxKind? unsupportedKind = null;
        foreach (var project in solution.Projects)
        {
            // Use Roslyn's pre-built declaration index instead of parsing every document's syntax
            // tree. FindDeclarationsAsync returns only source declarations (metadata excluded)
            // whose identifier matches typeName.
            var declarations = await SymbolFinder
                .FindDeclarationsAsync(project, typeName, ignoreCase: false, SymbolFilter.Type, ct)
                .ConfigureAwait(false);

            foreach (var symbol in declarations.OfType<INamedTypeSymbol>())
            {
                if (!string.Equals(symbol.Name, typeName, StringComparison.Ordinal)) continue;
                if (symbol.ContainingNamespace is not { IsGlobalNamespace: false } nsSymbol) continue;
                if (!string.Equals(NamespaceNameIdentity.Of(nsSymbol), fromKey, StringComparison.Ordinal)) continue;

                // Resolve the symbol back to its concrete syntax. A partial type surfaces one
                // symbol with multiple declaring references — enumerate each so a split declaration
                // still trips the ">1 match" ambiguity guard the caller relies on, exactly as the
                // previous per-TypeDeclaration walk did.
                foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
                {
                    var document = solution.GetDocument(syntaxRef.SyntaxTree);
                    if (document?.FilePath is null) continue;
                    // Count each declaration once, in the project we queried, so a source symbol
                    // shared into other project-reference compilations is not listed twice.
                    if (document.Project.Id != project.Id) continue;
                    var declaringSyntax = await syntaxRef.GetSyntaxAsync(ct).ConfigureAwait(false);
                    if (declaringSyntax is not TypeDeclarationSyntax typeDecl)
                    {
                        unsupportedKind ??= declaringSyntax.Kind();
                        continue;
                    }
                    if (await syntaxRef.SyntaxTree.GetRootAsync(ct).ConfigureAwait(false) is not CompilationUnitSyntax root) continue;
                    matches.Add((document, root, typeDecl, document.Project));
                }
            }
        }

        if (matches.Count == 0)
        {
            if (unsupportedKind is { } kind)
            {
                throw UnsupportedTypeKindRefusal.Create(kind, "change_type_namespace_preview",
                    UnsupportedTypeKindRefusal.AnyTypeDeclaration);
            }
            throw new InvalidOperationException(
                $"Type '{typeName}' in namespace '{fromNamespace}' was not found in the workspace.");
        }
        if (matches.Count > 1)
        {
            var files = string.Join(", ", matches.Select(m => m.Document.FilePath));
            throw new InvalidOperationException(
                $"Type '{typeName}' in namespace '{fromNamespace}' matched multiple files: {files}. " +
                "change_type_namespace_preview requires a unique match.");
        }

        return matches[0];
    }

    /// <summary>
    /// Resolve the destination file path. If the caller passes <paramref name="newFilePath"/>,
    /// validate it sits inside the source project's directory (workspace-root sandbox).
    /// Otherwise the source path is returned unchanged.
    /// </summary>
    private static string ResolveDestinationPath(string sourceFilePath, string? newFilePath, Project sourceProject)
    {
        if (string.IsNullOrWhiteSpace(newFilePath))
        {
            return sourceFilePath;
        }

        string fullDestination;
        try
        {
            fullDestination = Path.GetFullPath(newFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException)
        {
            throw ArgumentErrors.Redacted(nameof(newFilePath), "Destination path could not be normalized.", exception);
        }
        var projectFilePath = sourceProject.FilePath
            ?? throw new InvalidOperationException("Source project must have a file path on disk.");
        var projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{projectFilePath}'.");

        if (!FileSystemPath.IsStrictDescendant(projectDirectory, fullDestination))
        {
            throw new PublicArgumentException(
                "Destination file must be inside the source project directory.",
                nameof(newFilePath));
        }

        return fullDestination;
    }

    /// <summary>
    /// Apply the namespace rewrite to the source file. Returns the projected destination document,
    /// the moved type's identifier span, and a flag
    /// describing whether sibling types (enums and delegates included) in
    /// <paramref name="fromNamespace"/> remain in the solution after the move (used later to
    /// decide whether stale <c>using</c> directives are safe to drop). Without file relocation, any
    /// other member of the containing namespace declaration (type, enum, delegate, nested
    /// namespace) stays in the source namespace; only the selected type moves.
    /// </summary>
    private static async Task<(Document Destination, TextSpan TypeIdentifier, bool SiblingTypesRemaining)> ApplyNamespaceRewriteAsync(
        Solution solution,
        Document sourceDocument,
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax typeDecl,
        string fromNamespace,
        string toNamespace,
        string destinationFilePath,
        bool isRelocating,
        CancellationToken ct)
    {
        var containingNs = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot change namespace of a type with no enclosing namespace declaration.");

        // Renaming the containing declaration in place is correct only when it is a top-level
        // declaration (its name IS the full namespace; a nested one would keep its enclosing
        // names, turning Outer.Inner into Outer.Target) and the selected type is its only member:
        // any other member (class, struct, interface, record, enum, delegate, nested namespace)
        // would be dragged along. Namespace-level usings and externs are not members, so they
        // never force extraction.
        var canRenameInPlace = containingNs.Parent is CompilationUnitSyntax && containingNs.Members.Count == 1;

        // Tally sibling types (enums and delegates included) in fromNamespace across ANY other
        // document in the solution — used for consumer using-rewrite decisions (only drop
        // `using Old;` if nothing remains anywhere in Old). The source document's own remaining
        // declarations are folded in below, per path, from its updated root.
        var siblingTypesAnywhereInFromNs = await CountSiblingTypesInNamespaceAsync(
            solution, fromNamespace, excludeDocumentId: sourceDocument.Id, ct).ConfigureAwait(false);
        var siblingTypesRemaining = siblingTypesAnywhereInFromNs > 0;

        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Semantic model could not be created for the source document.");

        if (!isRelocating)
        {
            var movedType = new SyntaxAnnotation();
            var updatedRoot = canRenameInPlace
                ? RewriteNamespaceName(sourceRoot, containingNs, typeDecl, toNamespace, movedType, semanticModel, ct)
                : MoveTypeIntoNewNamespaceInSameFile(sourceRoot, typeDecl, toNamespace, movedType, semanticModel, ct);
            siblingTypesRemaining |= NamespaceMemberClassifier.DeclaresTypeLikeMemberIn(updatedRoot, fromNamespace);
            var updatedDocument = sourceDocument.WithSyntaxRoot(updatedRoot);
            if (!canRenameInPlace)
            {
                // Format only the synthesized namespace declarations so their layout follows editorconfig.
                updatedDocument = await Formatter.FormatAsync(
                    updatedDocument, Formatter.Annotation, options: null, cancellationToken: ct).ConfigureAwait(false);
                updatedRoot = await updatedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
                    ?? throw InvalidOperationErrors.Internal("The formatted destination syntax root is unavailable.");
            }
            var movedDeclaration = (TypeDeclarationSyntax)updatedRoot.GetAnnotatedNodes(movedType).Single();
            return (updatedDocument, movedDeclaration.Identifier.Span, siblingTypesRemaining);
        }

        // Relocating: write a new file for the relocated type and either remove the type from the
        // old file (if anything else remains in it) or remove the old file entirely.
        var removedTypeRoot = MovedDeclarationFileWriter.RemoveDeclaration(sourceRoot, typeDecl);
        Solution newSolution;
        if (!NamespaceMemberClassifier.HasRemainingContent(removedTypeRoot))
        {
            // Nothing but namespace shells, usings and trivia is left; delete the file.
            newSolution = solution.RemoveDocument(sourceDocument.Id);
        }
        else
        {
            newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, removedTypeRoot);
            // The retained file's own declarations (enums and delegates included, in any block of
            // fromNamespace) keep that namespace populated. The cross-document count above
            // excludes this document, so fold them in or consumers lose a `using` they still need.
            siblingTypesRemaining |= NamespaceMemberClassifier.DeclaresTypeLikeMemberIn(removedTypeRoot, fromNamespace);
        }

        // Build the new compilation unit for the moved type, wrapped in the destination namespace.
        var (newFileRoot, movedTypeIdentifier) = BuildMovedTypeCompilationUnit(sourceRoot, typeDecl, toNamespace,
            ReceivingNamespaceFactory.RequireCSharpOptions(sourceDocument.Project.ParseOptions), semanticModel, ct);
        var targetProject = newSolution.GetProject(sourceDocument.Project.Id)!;
        var targetFileName = Path.GetFileName(destinationFilePath);
        var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, destinationFilePath);
        var newDoc = targetProject.AddDocument(
            targetFileName,
            SourceText.From(newFileRoot.ToFullString()),
            folders: folders,
            filePath: destinationFilePath);
        return (newDoc, movedTypeIdentifier, siblingTypesRemaining);
    }

    private static CompilationUnitSyntax RewriteNamespaceName(
        CompilationUnitSyntax root,
        BaseNamespaceDeclarationSyntax ns,
        TypeDeclarationSyntax typeDecl,
        string toNamespace,
        SyntaxAnnotation movedType,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var newName = ReceivingNamespaceFactory.CreateName(toNamespace).WithTriviaFrom(ns.Name);
        // Imports written relative to the old namespace would rebind to the new one.
        var imports = MovedDeclarationFileWriter.PreserveImportBindings(ns.Usings, semanticModel, ct);
        var renamed = ns.ReplaceNode(typeDecl, typeDecl.WithAdditionalAnnotations(movedType))
            .WithName(newName).WithUsings(imports);
        return root.ReplaceNode(ns, renamed);
    }

    /// <summary>
    /// When the relocated type stays in the same file but cannot rename its containing declaration
    /// (other members remain, or the declaration is nested), move only that type into a new
    /// block-namespace declaration appended to the compilation unit. Every retained declaration
    /// stays at its original text position, so conditional-compilation directive pairs and their
    /// scope are untouched: the selected declaration is removed keeping its directives, and its
    /// moved copy carries every directive boundary it needs to stay balanced and conditional
    /// exactly as before. The appended block takes over the end-of-file trivia as its own leading
    /// trivia, so every directive that closes at the end of the file closes before the artificial
    /// namespace boundary rather than after it. A file-scoped source namespace is converted to
    /// block form first: C# forbids mixing file-scoped and block namespace declarations in one
    /// file (CS8955), and a file can hold at most one file-scoped declaration. Every synthesized
    /// declaration carries <see cref="Formatter.Annotation"/>; the caller must run
    /// <see cref="Formatter"/> over it.
    /// </summary>
    private static CompilationUnitSyntax MoveTypeIntoNewNamespaceInSameFile(
        CompilationUnitSyntax root,
        TypeDeclarationSyntax typeDecl,
        string toNamespace,
        SyntaxAnnotation movedType,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var endOfLine = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia)) is var eol
            && eol.IsKind(SyntaxKind.EndOfLineTrivia)
            ? eol
            : SyntaxFactory.EndOfLine(Environment.NewLine);

        // The moved copy keeps the type's own trivia (doc comments included), so none is left
        // behind in the retained file; the end-of-file trivia stays in the retained file.
        var (movedMember, namespaceUsings) = MovedDeclarationFileWriter.PrepareMovedMember(
            root, typeDecl, SyntaxFactory.List<UsingDirectiveSyntax>(), SyntaxFactory.TriviaList(),
            endOfFileTravelsWithMember: false, semanticModel, ct);

        var containingNs = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First();
        var outermostNs = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Last();
        var tracked = root.TrackNodes(outermostNs, containingNs, typeDecl);
        var retained = MovedDeclarationFileWriter.RemoveDeclaration(tracked, tracked.GetCurrentNode(typeDecl)!);

        // The retained block lost a member; let the formatter tidy its closing brace.
        if (retained.GetCurrentNode(containingNs) is NamespaceDeclarationSyntax block)
        {
            retained = retained.ReplaceNode(block, block.WithCloseBraceToken(
                block.CloseBraceToken.WithAdditionalAnnotations(Formatter.Annotation)));
        }

        var endOfFileTrivia = retained.EndOfFileToken.LeadingTrivia;
        var newBlockLeadingTrivia = SyntaxFactory.TriviaList(endOfLine);
        if (retained.GetCurrentNode(outermostNs) is FileScopedNamespaceDeclarationSyntax currentFileScoped)
        {
            var fileScoped = currentFileScoped;
            // Drop the blank line that followed `namespace X;` so the block opens cleanly.
            if (fileScoped.Externs.Count == 0 && fileScoped.Usings.Count == 0 &&
                fileScoped.Members.FirstOrDefault() is { } firstMember)
            {
                fileScoped = fileScoped.ReplaceNode(firstMember, firstMember.WithLeadingTrivia(
                    firstMember.GetLeadingTrivia().SkipWhile(IsWhitespaceOrEndOfLine)));
            }

            // The converted block spans the rest of the file, so its brace goes after the
            // end-of-file trivia (the directives that close there).
            var converted = SyntaxFactory.NamespaceDeclaration(
                    fileScoped.AttributeLists,
                    fileScoped.Modifiers,
                    fileScoped.NamespaceKeyword,
                    fileScoped.Name,
                    SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                        .WithTrailingTrivia(fileScoped.SemicolonToken.TrailingTrivia),
                    fileScoped.Externs,
                    fileScoped.Usings,
                    fileScoped.Members,
                    SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                        .WithLeadingTrivia(endOfFileTrivia)
                        .WithTrailingTrivia(endOfLine),
                    semicolonToken: default)
                .WithAdditionalAnnotations(Formatter.Annotation);
            retained = retained.ReplaceNode(currentFileScoped, converted);
        }
        else
        {
            newBlockLeadingTrivia = endOfFileTrivia.Add(endOfLine);
        }

        retained = retained.WithEndOfFileToken(retained.EndOfFileToken.WithLeadingTrivia(SyntaxFactory.TriviaList()));

        // Append a new block-namespace declaration for the relocated type, reusing the file's own
        // line terminator for the separator and the final newline.
        var relocatedType = movedMember
            .WithLeadingTrivia(movedMember.GetLeadingTrivia().SkipWhile(IsWhitespaceOrEndOfLine))
            .WithAdditionalAnnotations(movedType);
        var appended = SyntaxFactory.NamespaceDeclaration(ReceivingNamespaceFactory.CreateName(toNamespace))
            // The enclosing namespace scopes' imports stay with the retained declarations; the
            // moved copy gets its own so it keeps the bindings it was written against.
            .WithUsings(namespaceUsings)
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(relocatedType))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(endOfLine))
            .WithLeadingTrivia(newBlockLeadingTrivia)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return retained.AddMembers(appended);
    }

    private static bool IsWhitespaceOrEndOfLine(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia);

    private static (CompilationUnitSyntax Root, TextSpan TypeIdentifier) BuildMovedTypeCompilationUnit(
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax typeDecl,
        string toNamespace,
        CSharpParseOptions receivingOptions,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var destinationNs = MovedDeclarationFileWriter.FrameNamespace(
            ReceivingNamespaceFactory.Create(toNamespace, receivingOptions),
            SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.CarriageReturnLineFeed));
        return MovedDeclarationFileWriter.CreateCompilationUnit(
            sourceRoot, typeDecl, sourceRoot.Usings, destinationNs, semanticModel, ct);
    }

    /// <summary>
    /// Count source type declarations (classes, structs, interfaces, records, enums and delegates)
    /// that live in <paramref name="ns"/> anywhere in the solution, optionally excluding a specific
    /// document (typically the document we just mutated, whose remaining declarations are checked
    /// separately against its updated root).
    /// </summary>
    private static async Task<int> CountSiblingTypesInNamespaceAsync(
        Solution solution, string ns, DocumentId? excludeDocumentId, CancellationToken ct)
    {
        var count = 0;
        foreach (var project in solution.Projects)
        {
            // Resolve the namespace off the project's already-built compilation symbol table
            // instead of re-parsing every document. GetTypeMembers() returns only the types
            // directly in the namespace, matching the previous direct-namespace-member walk.
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var nsSymbol = NamespaceMemberClassifier.FindNamespaceSymbol(compilation.GlobalNamespace,
                NamespaceNameIdentity.Of(ns).Split('.', StringSplitOptions.RemoveEmptyEntries));
            if (nsSymbol is null) continue;

            foreach (var type in nsSymbol.GetTypeMembers())
            {
                foreach (var syntaxRef in type.DeclaringSyntaxReferences)
                {
                    var document = solution.GetDocument(syntaxRef.SyntaxTree);
                    if (document is null) continue;
                    // A namespace symbol merges declarations across project-reference compilations;
                    // count each source declaration once, in its owning project, and never for
                    // metadata types (which have no declaring syntax references).
                    if (document.Project.Id != project.Id) continue;
                    if (excludeDocumentId is not null && document.Id == excludeDocumentId) continue;
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Result of the consumer-using rewrite pass.
    /// </summary>
    private sealed record ConsumerUpdateResult(
        Solution Solution,
        IReadOnlyList<string> FallbackKeptOldUsingFiles);

    /// <summary>
    /// Rewrite the imports of every document that references the relocated type or imports the
    /// namespace it left. References are judged by binding, scope by scope: each reference site is
    /// resolved in the updated solution and measured against ITS OWN enclosing namespace chain, and
    /// every <c>using</c> list (the compilation unit and each namespace declaration) is rewritten
    /// on its own. Respects ambient-namespace resolution — a scope that shares an ancestor
    /// namespace with the old OR new location may not need an import change at all.
    /// </summary>
    private static async Task<ConsumerUpdateResult> UpdateConsumerUsingsAsync(
        Solution solution,
        string typeName,
        string fromNamespace,
        string toNamespace,
        bool siblingTypesRemaining,
        CancellationToken ct)
    {
        var fromKey = NamespaceNameIdentity.Of(fromNamespace);
        var toKey = NamespaceNameIdentity.Of(toNamespace);
        var fallbackFiles = new List<string>();
        // Every document is bound against the same snapshot; the edits are applied together after
        // the loop, because applying one would invalidate the compilations the next one binds in.
        var updates = new List<(DocumentId Id, CompilationUnitSyntax Root)>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                if (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) is not CompilationUnitSyntax root) continue;

                // Cheap syntactic pre-filter; the semantic work below decides what is a reference.
                var mentionsType = root.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Any(id => string.Equals(id.Identifier.ValueText, typeName, StringComparison.Ordinal));
                var importsOld = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                    .Any(directive => NamespaceNameIdentity.ImportsNamespace(directive, fromKey));
                if (!mentionsType && !importsOld) continue;

                var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
                    ?? throw InvalidOperationErrors.Internal("Semantic model could not be created for a consumer document.");
                var updatedRoot = AdjustImports(
                    root, semanticModel, typeName, fromKey, toNamespace, toKey, siblingTypesRemaining, ct, out var keptFallback);

                if (keptFallback && document.FilePath is not null)
                {
                    fallbackFiles.Add(document.FilePath);
                }

                if (!ReferenceEquals(updatedRoot, root))
                {
                    updates.Add((document.Id, updatedRoot));
                }
            }
        }

        foreach (var (id, updatedRoot) in updates)
        {
            solution = solution.WithDocumentSyntaxRoot(id, updatedRoot);
        }
        return new ConsumerUpdateResult(solution, fallbackFiles);
    }

    /// <summary>
    /// Ambient-resolution-aware, per-scope import rewrite. Rules:
    /// <list type="bullet">
    ///   <item><description>A reference site needs <c>using toNamespace</c> when its OWN enclosing namespace is not ambient to <paramref name="toKey"/> (equal to it or a descendant; ancestors DO need the using, C# lookup climbs ancestors only) and no enclosing scope already imports it. One import is added to the compilation unit when any site needs it.</description></item>
    ///   <item><description>A plain <c>using fromNamespace</c> in a scope is removed ONLY if no sibling types remain in that namespace and no namespace declared inside that scope is ambient to <paramref name="fromKey"/> (avoids touching an import that was already redundant before the move).</description></item>
    ///   <item><description>When in doubt, keep the <c>using</c> — band-aid removals risk breaking compile.</description></item>
    /// </list>
    /// </summary>
    private static CompilationUnitSyntax AdjustImports(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string typeName,
        string fromKey,
        string toNamespace,
        string toKey,
        bool siblingTypesRemaining,
        CancellationToken ct,
        out bool keptFallback)
    {
        keptFallback = false;
        var namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();

        var needsNewImport = !root.Usings.Any(directive => NamespaceNameIdentity.ImportsNamespace(directive, toKey))
            && root.DescendantNodes().OfType<IdentifierNameSyntax>().Any(site =>
                IsMovedTypeReference(site, typeName, toKey, semanticModel, ct) && NeedsImport(site, toKey));

        var rewrittenScopes = new Dictionary<BaseNamespaceDeclarationSyntax, SyntaxList<UsingDirectiveSyntax>>();
        foreach (var scope in namespaces)
        {
            var usings = RewriteScopeUsings(scope.Usings, scope, fromKey, siblingTypesRemaining, ref keptFallback);
            if (usings != scope.Usings) rewrittenScopes[scope] = usings;
        }

        var rootUsings = RewriteScopeUsings(root.Usings, root, fromKey, siblingTypesRemaining, ref keptFallback);
        if (needsNewImport)
        {
            // NOTE: SyntaxFactory.UsingDirective emits a keyword token without trailing trivia, so
            // the `using` keyword would glue onto the namespace name (`usingChild;`). We explicitly
            // construct the using-keyword token with a trailing space mirroring the pattern used by
            // CrossProjectRefactoringService's namespace-declaration token construction.
            rootUsings = rootUsings.Add(SyntaxFactory.UsingDirective(
                    usingKeyword: SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    staticKeyword: default,
                    alias: null,
                    name: ReceivingNamespaceFactory.CreateName(toNamespace),
                    semicolonToken: SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine)));
        }

        var newRoot = rewrittenScopes.Count == 0
            ? root
            : root.ReplaceNodes(rewrittenScopes.Keys, (original, rewritten) => rewritten.WithUsings(rewrittenScopes[original]));
        return rootUsings == root.Usings ? newRoot : newRoot.WithUsings(rootUsings);
    }

    /// <summary>
    /// True when <paramref name="site"/> is a simple-name use of the relocated type: it binds to the
    /// moved type, or to nothing at all (the old import no longer reaches it). A name that binds to
    /// any other symbol, a member-access or qualified right-hand side, or a name inside a using
    /// directive is not a reference whose scope needs an import.
    /// </summary>
    private static bool IsMovedTypeReference(
        IdentifierNameSyntax site, string typeName, string toKey, SemanticModel semanticModel, CancellationToken ct)
    {
        if (!string.Equals(site.Identifier.ValueText, typeName, StringComparison.Ordinal)) return false;
        if (site.Parent is QualifiedNameSyntax qualified && qualified.Right == site) return false;
        if (site.Parent is AliasQualifiedNameSyntax aliasQualified && aliasQualified.Name == site) return false;
        if (site.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == site) return false;
        if (site.Parent is MemberBindingExpressionSyntax) return false;
        if (site.Ancestors().OfType<UsingDirectiveSyntax>().Any()) return false;

        var info = semanticModel.GetSymbolInfo(site, ct);
        return info.Symbol is { } symbol
            ? symbol is INamedTypeSymbol type
                && string.Equals(type.Name, typeName, StringComparison.Ordinal)
                && string.Equals(NamespaceNameIdentity.Of(type.ContainingNamespace), toKey, StringComparison.Ordinal)
            : info.CandidateSymbols.IsEmpty;
    }

    /// <summary>
    /// True when a reference at <paramref name="site"/> cannot see the destination namespace: its own
    /// enclosing namespace is not ambient to it and no enclosing scope imports it.
    /// </summary>
    private static bool NeedsImport(SyntaxNode site, string toKey)
    {
        var enclosing = site.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().ToList();
        if (enclosing.Count > 0 && IsAmbientTo(QualifiedNamespaceName(enclosing[0]), toKey)) return false;
        return !enclosing.Any(scope => scope.Usings.Any(directive => NamespaceNameIdentity.ImportsNamespace(directive, toKey)));
    }

    /// <summary>
    /// Applies the old-import decision to one <c>using</c> list. Keeps the import (and flags the
    /// fallback) while sibling types remain or the scope is itself ambient to the old namespace.
    /// </summary>
    private static SyntaxList<UsingDirectiveSyntax> RewriteScopeUsings(
        SyntaxList<UsingDirectiveSyntax> usings,
        SyntaxNode scope,
        string fromKey,
        bool siblingTypesRemaining,
        ref bool keptFallback)
    {
        var oldImports = usings.Where(directive => NamespaceNameIdentity.ImportsNamespace(directive, fromKey)).ToList();
        if (oldImports.Count == 0) return usings;

        var ambientToOld = scope.DescendantNodesAndSelf().OfType<BaseNamespaceDeclarationSyntax>()
            .Any(declared => IsAmbientTo(QualifiedNamespaceName(declared), fromKey));
        if (siblingTypesRemaining || ambientToOld)
        {
            // Sibling types still in the old namespace may be referenced by this scope, or the
            // import was already redundant before the move: keep it and record it for the warning.
            keptFallback = true;
            return usings;
        }

        // Old namespace is empty after the move and this scope cannot see it ambiently: drop it.
        return SyntaxFactory.List(usings.Where(directive => !oldImports.Contains(directive)));
    }

    /// <summary>Full dotted name of a namespace declaration, including the declarations it is nested in.</summary>
    private static string QualifiedNamespaceName(BaseNamespaceDeclarationSyntax declaration) =>
        string.Join('.', declaration.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(declared => NamespaceNameIdentity.Of(declared.Name)));

    /// <summary>
    /// C# ambient-namespace resolution: a file whose namespace is <c>A.B.C</c> can resolve
    /// symbols declared in <c>A.B.C</c>, <c>A.B</c>, <c>A</c>, and the global namespace without
    /// any <c>using</c>. Lookup climbs ancestors only — a file in <c>A.B</c> cannot resolve
    /// <c>A.B.C</c> symbols ambiently and still requires <c>using A.B.C;</c>. Returns
    /// <see langword="true"/> only when the consumer's namespace equals the target or is a
    /// strict descendant of it.
    /// </summary>
    private static bool IsAmbientTo(string consumerNamespace, string targetNamespace)
    {
        if (string.IsNullOrEmpty(consumerNamespace) || string.IsNullOrEmpty(targetNamespace))
        {
            return false;
        }
        if (string.Equals(consumerNamespace, targetNamespace, StringComparison.Ordinal))
        {
            return true;
        }
        // Consumer's namespace is a strict descendant of the target (e.g. consumer A.B.C sees A.B).
        // Note: a consumer that is an ANCESTOR of the target (e.g. consumer A.B referencing a type
        // in A.B.C) is NOT ambient — C# lookup climbs ancestors, it does not descend — so the
        // consumer still needs `using A.B.C;`.
        if (consumerNamespace.StartsWith(targetNamespace + ".", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }
}
