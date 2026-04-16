using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class TypeMoveService : ITypeMoveService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<TypeMoveService> _logger;

    public TypeMoveService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<TypeMoveService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewMoveTypeToFileAsync(
        string workspaceId, string sourceFilePath, string typeName, string? targetFilePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, sourceFilePath)
            ?? throw new InvalidOperationException($"Document not found: {sourceFilePath}");

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var typeDecl = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' not found in {sourceFilePath}.");

        // Validate source file has more than one type (otherwise move is pointless)
        var typeCount = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Count(t => t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax);
        if (typeCount < 2)
        {
            throw new InvalidOperationException(
                "Source file only contains one top-level type. " +
                "Nested types cannot be extracted with this tool — only top-level type declarations are considered.");
        }

        // Determine target file path
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
        var resolvedTargetPath = targetFilePath ?? Path.Combine(sourceDir, $"{typeName}.cs");
        resolvedTargetPath = Path.GetFullPath(resolvedTargetPath);

        // Check if target file already exists in the solution
        if (solution.Projects.SelectMany(p => p.Documents)
            .Any(d => d.FilePath is not null &&
                      string.Equals(Path.GetFullPath(d.FilePath), resolvedTargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Target file already exists: {resolvedTargetPath}");
        }

        // Strip private/protected modifiers from top-level types (invalid at namespace level)
        var movedTypeDecl = StripInvalidTopLevelModifiers(typeDecl);

        // Build the new file content
        var usings = sourceRoot.Usings;
        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        CompilationUnitSyntax newFileRoot;
        if (namespaceDecl is FileScopedNamespaceDeclarationSyntax fileScopedNs)
        {
            // File-scoped namespace: recreate it with just the type
            var newNs = SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                .WithNamespaceKeyword(fileScopedNs.NamespaceKeyword)
                .WithSemicolonToken(fileScopedNs.SemicolonToken)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(movedTypeDecl));

            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else if (namespaceDecl is NamespaceDeclarationSyntax blockNs)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(movedTypeDecl));

            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else
        {
            // Top-level type (no namespace)
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(movedTypeDecl))
                .NormalizeWhitespace();
        }

        // NormalizeWhitespace() can introduce a stray leading blank line and may inflate the
        // separator between the using block and the first member. Canonicalize both before
        // serializing to disk.
        newFileRoot = TriviaNormalizationHelper.NormalizeLeadingTrivia(newFileRoot);
        newFileRoot = TriviaNormalizationHelper.NormalizeUsingToMemberSeparator(newFileRoot);

        // Remove the type from the source file
        var updatedSourceRoot = sourceRoot.RemoveNode(typeDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;

        // Apply changes to solution
        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add the new document.
        // Item #1 — severity-critical-fail-preview-diff-does-not-match-t: pass `folders`
        // so MSBuildWorkspace.TryApplyChanges computes the disk path consistently with
        // our explicit write in RefactoringService.PersistDocumentSetChangesAsync.
        // Without folders, Roslyn resolved the AddedDocument to {projectDir}/{fileName}
        // while our explicit write used the full resolvedTargetPath — producing two files
        // on disk (the intended deep path plus a rogue project-root copy) per the
        // NetworkDocumentation audit §9.2 repro.
        var targetFileName = Path.GetFileName(resolvedTargetPath);
        var newFileText = newFileRoot.ToFullString();
        var targetProject = newSolution.GetProject(sourceDocument.Project.Id)!;
        var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, resolvedTargetPath);
        var newDocument = targetProject.AddDocument(targetFileName, newFileText, folders: folders, filePath: resolvedTargetPath);
        newSolution = newDocument.Project.Solution;

        // BUG-N13: source file may rely on ImplicitUsings for generic collections; new file must carry explicit usings when needed.
        newSolution = await EnsureCollectionsGenericUsingIfNeededAsync(newSolution, newDocument.Id, movedTypeDecl, ct)
            .ConfigureAwait(false);

        // Remove unnecessary usings from the new file by checking for CS8019 diagnostics
        newSolution = await RemoveUnusedUsingsAsync(newSolution, newDocument.Id, ct).ConfigureAwait(false);

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Move type '{typeName}' to {targetFileName}";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static async Task<Solution> EnsureCollectionsGenericUsingIfNeededAsync(
        Solution solution, DocumentId documentId, TypeDeclarationSyntax movedType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var typeText = movedType.ToFullString();
        var needsGeneric = Regex.IsMatch(
            typeText,
            @"\b(Dictionary|List|HashSet|IEnumerable|ICollection|IReadOnlyList|IReadOnlyDictionary|IReadOnlySet|Queue|Stack|LinkedList|SortedDictionary|SortedList|ConcurrentDictionary|ConcurrentBag|ObservableCollection)<",
            RegexOptions.CultureInvariant);
        if (!needsGeneric)
            return solution;

        var document = solution.GetDocument(documentId);
        if (document is null)
            return solution;

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null)
            return solution;

        var already = root.Usings.Any(u =>
            u.Name?.ToString().Equals("System.Collections.Generic", StringComparison.Ordinal) == true);
        if (already)
            return solution;

        var usingDir = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic"))
            .NormalizeWhitespace();
        var newRoot = root.WithUsings(root.Usings.Add(usingDir));
        return solution.WithDocumentSyntaxRoot(documentId, newRoot);
    }

    private static async Task<Solution> RemoveUnusedUsingsAsync(Solution solution, DocumentId documentId, CancellationToken ct)
    {
        var document = solution.GetDocument(documentId);
        if (document is null) return solution;

        try
        {
            var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) return solution;

            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (tree is null || root is null) return solution;

            var unusedUsings = compilation.GetDiagnostics(ct)
                .Where(d => d.Id == "CS8019" && d.Location.SourceTree == tree)
                .Select(d => root.FindNode(d.Location.SourceSpan))
                .OfType<UsingDirectiveSyntax>()
                .Distinct()
                .ToList();

            if (unusedUsings.Count > 0)
            {
                root = root.RemoveNodes(unusedUsings, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
                if (root is CompilationUnitSyntax cu)
                {
                    cu = TriviaNormalizationHelper.NormalizeLeadingTrivia(cu);
                    cu = TriviaNormalizationHelper.CollapseBlankLinesInUsingBlock(cu);
                    root = cu;
                }
                solution = solution.WithDocumentSyntaxRoot(documentId, root);
            }
        }
        catch
        {
            // If unused-using detection fails, keep all usings — safe fallback
        }

        return solution;
    }

    private static TypeDeclarationSyntax StripInvalidTopLevelModifiers(TypeDeclarationSyntax typeDecl)
    {
        var invalidModifiers = new[] { SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword };
        var newModifiers = typeDecl.Modifiers.Where(m => !invalidModifiers.Contains(m.Kind()));
        var modifierList = SyntaxFactory.TokenList(newModifiers);

        // If stripping removed all access modifiers and type had none originally visible, add internal
        if (!modifierList.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword)))
        {
            modifierList = modifierList.Insert(0, SyntaxFactory.Token(SyntaxKind.InternalKeyword).WithTrailingTrivia(SyntaxFactory.Space));
        }

        return typeDecl.WithModifiers(modifierList);
    }
}
