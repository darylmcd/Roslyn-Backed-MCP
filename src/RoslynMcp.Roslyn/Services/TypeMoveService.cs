using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class TypeMoveService : ITypeMoveService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public TypeMoveService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewMoveTypeToFileAsync(
        string workspaceId, string sourceFilePath, string typeName, string? targetFilePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, sourceFilePath)
            ?? throw new PublicInvalidOperationException("Source document was not found in the workspace. Use workspace_list and document lookup tools to select a loaded C# source file.");

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new PublicInvalidOperationException("Source document must be a C# compilation unit. Select a C# source file and retry.");

        var declarations = sourceRoot.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .Where(t => t switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.ValueText == typeName,
                DelegateDeclarationSyntax type => type.Identifier.ValueText == typeName,
                _ => false,
            })
            .ToArray();

        if (declarations.Length > 1)
            throw new PublicInvalidOperationException("Type name is ambiguous in the source document. Use document_symbols to select a file with one matching declaration.");

        var declaration = declarations.SingleOrDefault();
        if (declaration is not null && declaration.Parent is not (CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
            throw new PublicInvalidOperationException("Nested types cannot be moved to a top-level file without changing their identity. Select a top-level declaration with document_symbols.");

        var typeDecl = declaration as TypeDeclarationSyntax;

        if (typeDecl is null)
        {
            // Distinguish "symbol found but unsupported kind" from "symbol not found".
            // EnumDeclarationSyntax derives from BaseTypeDeclarationSyntax (not TypeDeclarationSyntax);
            // DelegateDeclarationSyntax is a MemberDeclarationSyntax. Both are addressable by name from
            // symbol_search, so callers reasonably expect the move tool to handle them — surface a
            // structured error that names the resolved kind instead of the misleading "not found" text.
            var unsupportedKind = declaration?.Kind();

            if (unsupportedKind is not null)
            {
                var typeKindName = unsupportedKind switch
                {
                    SyntaxKind.EnumDeclaration => "Enum",
                    SyntaxKind.DelegateDeclaration => "Delegate",
                    _ => unsupportedKind.ToString()!,
                };
                throw new PublicInvalidOperationException(
                    $"type-kind {typeKindName} not supported for this refactor. " +
                    $"move_type_to_file_preview currently supports class, struct, record, and interface declarations only.");
            }

            throw new PublicInvalidOperationException("Type was not found in the source document. Use document_symbols to select a declaration from that file.");
        }

        // Validate source file has more than one type (otherwise move is pointless)
        var typeCount = sourceRoot.DescendantNodes()
            .Where(t => t is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Count(t => t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax);
        if (typeCount < 2)
        {
            throw new PublicInvalidOperationException(
                "Source file contains only one top-level type. " +
                "To move or rename the file, use move_file_preview instead. " +
                "move_type_to_file_preview is for extracting one type out of a file that contains multiple top-level types.");
        }

        // Determine target file path
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath)
            ?? throw new PublicInvalidOperationException("Source document must have an on-disk path. Select a saved C# source file and retry.");
        var resolvedTargetPath = targetFilePath ?? Path.Combine(sourceDir, $"{typeName}.cs");
        resolvedTargetPath = Path.GetFullPath(resolvedTargetPath);

        // Check if target file already exists in the solution
        if (solution.Projects.SelectMany(p => p.Documents)
            .Any(d => d.FilePath is not null &&
                      string.Equals(Path.GetFullPath(d.FilePath), resolvedTargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PublicInvalidOperationException("Target file already exists in the workspace. Choose a different targetFilePath and retry.");
        }

        var newFileRoot = CreateMovedCompilationUnit(sourceRoot, typeDecl);

        // NormalizeWhitespace() can introduce a stray leading blank line and may inflate the
        // separator between the using block and the first member. Canonicalize both before
        // serializing to disk.
        newFileRoot = TriviaNormalizationHelper.NormalizeLeadingTrivia(newFileRoot);
        newFileRoot = TriviaNormalizationHelper.NormalizeUsingToMemberSeparator(newFileRoot);

        // Remove the type from the source file
        var updatedSourceRoot = sourceRoot.RemoveNode(typeDecl, SyntaxRemoveOptions.KeepLeadingTrivia)
            ?? throw new InvalidOperationException("Removing a type unexpectedly removed its compilation unit.");

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
        var targetProject = newSolution.GetProject(sourceDocument.Project.Id)
            ?? throw new InvalidOperationException("The source project disappeared while preparing the type move.");
        var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, resolvedTargetPath);
        var newDocument = targetProject.AddDocument(targetFileName, newFileText, folders: folders, filePath: resolvedTargetPath);
        newSolution = newDocument.Project.Solution;

        // Remove unnecessary usings from the new file by checking for CS8019 diagnostics
        newSolution = await RemoveUnusedUsingsAsync(newSolution, newDocument.Id, ct).ConfigureAwait(false);

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var description = $"Move type '{typeName}' to {targetFileName}";
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            PreviewKind.MoveTypeToFile);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static CompilationUnitSyntax CreateMovedCompilationUnit(
        CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax typeDecl)
    {
        // Keep each namespace scope intact: flattening names or hoisting imports changes
        // alias, relative namespace, static import, and extern-alias binding.
        MemberDeclarationSyntax member = typeDecl;
        foreach (var scope in typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
            member = scope.WithMembers(SyntaxFactory.SingletonList(member));

        // Global (including implicit) usings already cover every document in this project.
        return SyntaxFactory.CompilationUnit()
            .WithExterns(sourceRoot.Externs)
            .WithUsings(SyntaxFactory.List(sourceRoot.Usings.Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))))
            .WithMembers(SyntaxFactory.SingletonList(member))
            .NormalizeWhitespace();
    }

    internal static async Task<Solution> RemoveUnusedUsingsAsync(Solution solution, DocumentId documentId, CancellationToken ct)
    {
        var document = solution.GetDocument(documentId);
        if (document is null) return solution;

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
        // Fail before publishing a preview; cancellation and unexpected failures retain
        // their identity for the host's established error/diagnostic boundary.
        return solution;
    }
}
