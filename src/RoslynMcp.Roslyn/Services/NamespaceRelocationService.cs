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
    /// Rewrite <c>using</c> directives across every document that references the relocated type.
    /// Respects ambient-namespace resolution — consumers that share an ancestor namespace with
    /// the old OR new location may not need a <c>using</c> change at all.
    /// </summary>
    private static async Task<ConsumerUpdateResult> UpdateConsumerUsingsAsync(
        Solution solution,
        string typeName,
        string fromNamespace,
        string toNamespace,
        bool siblingTypesRemaining,
        CancellationToken ct)
    {
        var fallbackFiles = new List<string>();
        foreach (var project in solution.Projects.ToList())
        {
            foreach (var document in project.Documents.ToList())
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
                if (root is null) continue;

                if (!DocumentReferencesType(root, typeName)) continue;

                var consumerNs = GetDocumentPrimaryNamespace(root);
                var updatedRoot = AdjustUsings(
                    root,
                    consumerNs,
                    fromNamespace,
                    toNamespace,
                    siblingTypesRemaining,
                    out var keptFallback);

                if (keptFallback && document.FilePath is not null)
                {
                    fallbackFiles.Add(document.FilePath);
                }

                if (!ReferenceEquals(updatedRoot, root))
                {
                    solution = solution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
                }
            }
        }
        return new ConsumerUpdateResult(solution, fallbackFiles);
    }

    /// <summary>
    /// Cheap textual pre-filter for the consumer pass — returns true if the document contains an
    /// identifier matching <paramref name="typeName"/>. False positives are fine: <see
    /// cref="AdjustUsings"/> is a no-op when no <c>using</c> rewrite is needed.
    /// </summary>
    private static bool DocumentReferencesType(CompilationUnitSyntax root, string typeName)
    {
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (string.Equals(id.Identifier.ValueText, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetDocumentPrimaryNamespace(CompilationUnitSyntax root)
    {
        return root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault() is { } primary ? NamespaceNameIdentity.Of(primary.Name) : string.Empty;
    }

    /// <summary>
    /// Ambient-resolution-aware using rewrite. Rules:
    /// <list type="bullet">
    ///   <item><description>If the consumer's namespace equals or is a descendant of <paramref name="toNamespace"/> (ambient resolution covers the new location), no <c>using toNamespace</c> is needed. Ancestor consumers (e.g. consumer in <c>A.B</c>, target <c>A.B.C</c>) DO need the using — C# lookup climbs ancestors only.</description></item>
    ///   <item><description>Otherwise: add <c>using toNamespace</c> if missing.</description></item>
    ///   <item><description>Remove <c>using fromNamespace</c> ONLY if no sibling types remain in that namespace and the consumer's own namespace is NOT ambient to <paramref name="fromNamespace"/> (avoids flagging a using that was already redundant before the move).</description></item>
    ///   <item><description>When in doubt, keep the <c>using</c> — band-aid removals risk breaking compile.</description></item>
    /// </list>
    /// </summary>
    private static CompilationUnitSyntax AdjustUsings(
        CompilationUnitSyntax root,
        string consumerNamespace,
        string fromNamespace,
        string toNamespace,
        bool siblingTypesRemaining,
        out bool keptFallback)
    {
        keptFallback = false;
        var fromKey = NamespaceNameIdentity.Of(fromNamespace);
        var toKey = NamespaceNameIdentity.Of(toNamespace);
        var hasNewUsing = root.Usings.Any(u => NamespaceNameIdentity.ImportsNamespace(u, toKey));
        var oldUsing = root.Usings.FirstOrDefault(u => NamespaceNameIdentity.ImportsNamespace(u, fromKey));

        var consumerKey = NamespaceNameIdentity.Of(consumerNamespace);
        var consumerCoversNewByAmbient = IsAmbientTo(consumerKey, toKey);
        var consumerCoversOldByAmbient = IsAmbientTo(consumerKey, fromKey);

        var newRoot = root;

        // Add `using toNamespace;` if the consumer cannot resolve the type ambiently and no using
        // already exists. NOTE: SyntaxFactory.UsingDirective emits a keyword token without
        // trailing trivia, so the `using` keyword would glue onto the namespace name
        // (`usingChild;`). We explicitly construct the using-keyword token with a trailing space
        // mirroring the pattern used by CrossProjectRefactoringService's namespace-declaration
        // token construction.
        if (!consumerCoversNewByAmbient && !hasNewUsing)
        {
            var addUsing = SyntaxFactory.UsingDirective(
                    usingKeyword: SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    staticKeyword: default,
                    alias: null,
                    name: ReceivingNamespaceFactory.CreateName(toNamespace),
                    semicolonToken: SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));
            newRoot = newRoot.WithUsings(newRoot.Usings.Add(addUsing));
        }

        // Decide whether to drop `using fromNamespace;`.
        if (oldUsing is not null)
        {
            if (siblingTypesRemaining)
            {
                // Sibling types still in the old namespace may be referenced by this file.
                // Keep the using to be safe and record it so the caller can warn.
                keptFallback = true;
            }
            else if (consumerCoversOldByAmbient)
            {
                // The using was already redundant before the move — leave it alone to avoid
                // surprising the author. This mirrors the plan's conservative guideline.
                keptFallback = true;
            }
            else
            {
                // Old namespace is empty after the move and the consumer can't see it ambiently.
                // Safe to drop the using.
                var updatedOldReference = newRoot.Usings.FirstOrDefault(u => NamespaceNameIdentity.ImportsNamespace(u, fromKey));
                if (updatedOldReference is not null)
                {
                    newRoot = newRoot.WithUsings(newRoot.Usings.Remove(updatedOldReference));
                }
            }
        }

        return newRoot;
    }

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
