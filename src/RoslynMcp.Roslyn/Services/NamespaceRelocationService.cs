using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Provides preview operations for relocating a type between namespaces inside the same
/// project. Rewrites the type's <c>namespace</c> declaration, optionally moves the file to a
/// new path inside the project, and adjusts consumer <c>using</c> directives respecting
/// ambient-namespace resolution (consumers in an ancestor/descendant of the source or target
/// namespace may need no <c>using</c> change at all).
/// </summary>
/// <remarks>
/// Pairs with <c>get_namespace_dependencies</c> to close circular namespace dependencies.
/// For example, moving <c>DevicePlatform</c> from <c>Child.Sub</c> to <c>Child</c> can break
/// a cycle where <c>Child</c> imports <c>Child.Sub</c> which in turn references <c>Child</c>.
/// </remarks>
public interface INamespaceRelocationService
{
    /// <summary>
    /// Previews relocating a type between namespaces inside the same project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier returned by <c>workspace_load</c>.</param>
    /// <param name="typeName">Name of the type to relocate (must be unique within <paramref name="fromNamespace"/>).</param>
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
            throw new ArgumentException("Type name must be provided.", nameof(typeName));
        }
        if (string.IsNullOrWhiteSpace(fromNamespace))
        {
            throw new ArgumentException("Source namespace must be provided (use a file-move tool if the type currently has no namespace).", nameof(fromNamespace));
        }
        if (string.IsNullOrWhiteSpace(toNamespace))
        {
            throw new ArgumentException("Destination namespace must be provided.", nameof(toNamespace));
        }
        if (string.Equals(fromNamespace, toNamespace, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Source and destination namespaces must differ.");
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Locate the type by (namespace + name). Must resolve uniquely across the project that
        // owns the source file. We search every document in every project for a TypeDeclaration
        // whose name matches AND whose enclosing namespace matches fromNamespace.
        var (sourceDocument, sourceRoot, typeDecl, sourceProject) =
            await FindTypeInNamespaceAsync(solution, typeName, fromNamespace, ct).ConfigureAwait(false);

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

        // Build the updated source-file syntax. Three cases:
        //   (a) the source file's namespace declaration IS fromNamespace and contains only this
        //       type -> rewrite the namespace name in place.
        //   (b) the source file's namespace IS fromNamespace but contains other types -> if we
        //       are also relocating, extract the type into the new file; otherwise rewrite
        //       namespace declaration for the whole file (co-located types move together).
        //   (c) the source file has nested namespaces or the type sits in a deeper namespace -
        //       we reject with a clear error for the initial scope (flagged as a warning).
        var (updatedSolution, siblingTypesRemaining) = await ApplyNamespaceRewriteAsync(
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
            updatedSolution,
            typeName,
            fromNamespace,
            toNamespace,
            siblingTypesRemaining,
            ct).ConfigureAwait(false);
        updatedSolution = consumerUpdates.Solution;

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
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is null) continue;
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
                if (root is null) continue;

                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (!string.Equals(type.Identifier.ValueText, typeName, StringComparison.Ordinal)) continue;
                    var containing = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                    var ns = containing?.Name.ToString() ?? string.Empty;
                    if (string.Equals(ns, fromNamespace, StringComparison.Ordinal))
                    {
                        matches.Add((document, root, type, project));
                    }
                }
            }
        }

        if (matches.Count == 0)
        {
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

        var fullDestination = Path.GetFullPath(newFilePath);
        var projectFilePath = sourceProject.FilePath
            ?? throw new InvalidOperationException("Source project must have a file path on disk.");
        var projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{projectFilePath}'.");

        if (!fullDestination.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Destination path '{fullDestination}' must be inside the source project directory '{projectDirectory}'.",
                nameof(newFilePath));
        }

        return fullDestination;
    }

    /// <summary>
    /// Apply the namespace rewrite to the source file. Returns the updated solution and a flag
    /// describing whether sibling types in <paramref name="fromNamespace"/> remain in the
    /// solution after the move (used later to decide whether stale <c>using</c> directives are
    /// safe to drop).
    /// </summary>
    private static async Task<(Solution Solution, bool SiblingTypesRemaining)> ApplyNamespaceRewriteAsync(
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

        // Count other types in the same file that live inside the same fromNamespace
        // declaration (sibling types).
        var siblingTypesInSameNs = containingNs.Members.OfType<TypeDeclarationSyntax>()
            .Where(t => !ReferenceEquals(t, typeDecl))
            .ToList();

        // Tally sibling types in fromNamespace across ANY document in the solution — used for
        // consumer using-rewrite decisions (only drop `using Old;` if no sibling types remain
        // anywhere in Old).
        var siblingTypesAnywhereInFromNs = await CountSiblingTypesInNamespaceAsync(
            solution, fromNamespace, excludeDocumentId: sourceDocument.Id, ct).ConfigureAwait(false);
        // Plus remaining siblings inside the source file itself.
        var totalSiblingsAfterMove = siblingTypesAnywhereInFromNs + siblingTypesInSameNs.Count;
        var siblingTypesRemaining = totalSiblingsAfterMove > 0;

        if (!isRelocating && siblingTypesInSameNs.Count == 0)
        {
            // Simple case: rewrite the namespace declaration in place; no siblings to worry about.
            var rewritten = RewriteNamespaceName(sourceRoot, containingNs, toNamespace);
            return (solution.WithDocumentSyntaxRoot(sourceDocument.Id, rewritten), siblingTypesRemaining);
        }

        if (!isRelocating && siblingTypesInSameNs.Count > 0)
        {
            // No relocation but sibling types remain in the same declaration. We must extract the
            // type into its own namespace declaration within the same file.
            var updatedRoot = MoveTypeIntoNewNamespaceInSameFile(sourceRoot, containingNs, typeDecl, toNamespace);
            return (solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedRoot), siblingTypesRemaining);
        }

        // Relocating: write a new file for the relocated type and either remove the type from the
        // old file (if siblings remain) or remove the old file entirely (if no siblings).
        var removedTypeRoot = sourceRoot.RemoveNode(typeDecl, SyntaxRemoveOptions.KeepLeadingTrivia);
        Solution newSolution;
        if (siblingTypesInSameNs.Count == 0 && sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Count(t => t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax) == 1)
        {
            // Old file had only this type; delete it.
            newSolution = solution.RemoveDocument(sourceDocument.Id);
        }
        else
        {
            newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, removedTypeRoot!);
        }

        // Build the new compilation unit for the moved type, wrapped in the destination namespace.
        var newFileRoot = BuildMovedTypeCompilationUnit(sourceRoot, containingNs, typeDecl, toNamespace);
        var targetProject = newSolution.GetProject(sourceDocument.Project.Id)!;
        var targetFileName = Path.GetFileName(destinationFilePath);
        var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, destinationFilePath);
        var newDoc = targetProject.AddDocument(
            targetFileName,
            SourceText.From(newFileRoot.ToFullString()),
            folders: folders,
            filePath: destinationFilePath);
        return (newDoc.Project.Solution, siblingTypesRemaining);
    }

    private static CompilationUnitSyntax RewriteNamespaceName(
        CompilationUnitSyntax root,
        BaseNamespaceDeclarationSyntax ns,
        string toNamespace)
    {
        var newName = SyntaxFactory.ParseName(toNamespace).WithTriviaFrom(ns.Name);
        return root.ReplaceNode(ns, ns.WithName(newName));
    }

    /// <summary>
    /// When the source file keeps other types but the relocated type stays in the same file,
    /// extract the target type into its own namespace declaration appended to the compilation
    /// unit. Both the existing file-scoped / block namespace and the new sibling namespace
    /// remain readable after rewrite.
    /// </summary>
    private static CompilationUnitSyntax MoveTypeIntoNewNamespaceInSameFile(
        CompilationUnitSyntax root,
        BaseNamespaceDeclarationSyntax ns,
        TypeDeclarationSyntax typeDecl,
        string toNamespace)
    {
        // Remove the type from its current namespace.
        var trimmedNs = ns.RemoveNode(typeDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;
        var rootAfterRemove = root.ReplaceNode(ns, trimmedNs);

        // Append a new block-namespace declaration for the relocated type. Block syntax is used
        // because a single file can have at most one file-scoped namespace declaration; if the
        // existing one is file-scoped we must fall back to a block-scoped sibling.
        var appended = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(toNamespace))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDecl))
            .WithLeadingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

        return rootAfterRemove.AddMembers(appended);
    }

    /// <summary>
    /// Build a fresh compilation unit for the relocated type with a file-scoped destination
    /// namespace declaration. Preserves the source file's usings.
    /// </summary>
    private static CompilationUnitSyntax BuildMovedTypeCompilationUnit(
        CompilationUnitSyntax sourceRoot,
        BaseNamespaceDeclarationSyntax containingNs,
        TypeDeclarationSyntax typeDecl,
        string toNamespace)
    {
        _ = containingNs; // shape retained for future namespace-using filtering
        var destinationNs = SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(toNamespace))
            .WithNamespaceKeyword(SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.CarriageReturnLineFeed))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDecl));

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(sourceRoot.Usings)
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(destinationNs));

        compilationUnit = TriviaNormalizationHelper.NormalizeLeadingTrivia(compilationUnit);
        compilationUnit = TriviaNormalizationHelper.NormalizeUsingToMemberSeparator(compilationUnit);
        return compilationUnit;
    }

    /// <summary>
    /// Count type declarations that live in <paramref name="ns"/> anywhere in the solution,
    /// optionally excluding a specific document (typically the document we just mutated, whose
    /// updated sibling count is tracked separately).
    /// </summary>
    private static async Task<int> CountSiblingTypesInNamespaceAsync(
        Solution solution, string ns, DocumentId? excludeDocumentId, CancellationToken ct)
    {
        var count = 0;
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (excludeDocumentId is not null && document.Id == excludeDocumentId) continue;
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
                if (root is null) continue;
                foreach (var nsDecl in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
                {
                    if (!string.Equals(nsDecl.Name.ToString(), ns, StringComparison.Ordinal)) continue;
                    count += nsDecl.Members.OfType<TypeDeclarationSyntax>().Count();
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
            .FirstOrDefault()?.Name.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Ambient-resolution-aware using rewrite. Rules:
    /// <list type="bullet">
    ///   <item><description>If the consumer's namespace is an ancestor/descendant of <paramref name="toNamespace"/> (ambient resolution covers the new location), no <c>using toNamespace</c> is needed.</description></item>
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
        var hasNewUsing = root.Usings.Any(u => string.Equals(u.Name?.ToString(), toNamespace, StringComparison.Ordinal));
        var oldUsing = root.Usings.FirstOrDefault(u => string.Equals(u.Name?.ToString(), fromNamespace, StringComparison.Ordinal));

        var consumerCoversNewByAmbient = IsAmbientTo(consumerNamespace, toNamespace);
        var consumerCoversOldByAmbient = IsAmbientTo(consumerNamespace, fromNamespace);

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
                    name: SyntaxFactory.ParseName(toNamespace),
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
                var updatedOldReference = newRoot.Usings.FirstOrDefault(u =>
                    string.Equals(u.Name?.ToString(), fromNamespace, StringComparison.Ordinal));
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
    /// any <c>using</c>. We also treat descendant namespaces as ambient when they contain the
    /// target (for example <c>A.B.C.D</c> is visible from <c>A.B.C</c>) — a conservative
    /// extension that matches the plan's "when in doubt keep the using" rule.
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
        // Consumer's namespace is an ancestor of the target.
        if (consumerNamespace.StartsWith(targetNamespace + ".", StringComparison.Ordinal))
        {
            return true;
        }
        // Consumer's namespace is a descendant of the target (e.g. consumer A.B.C.D sees A.B.C).
        if (targetNamespace.StartsWith(consumerNamespace + ".", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }
}
