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
                $"Source file only contains one type. Move is unnecessary.");
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
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDecl.WithLeadingTrivia(SyntaxFactory.ElasticLineFeed)));

            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else if (namespaceDecl is NamespaceDeclarationSyntax blockNs)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDecl));

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
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDecl))
                .NormalizeWhitespace();
        }

        // Remove the type from the source file
        var updatedSourceRoot = sourceRoot.RemoveNode(typeDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;

        // Apply changes to solution
        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add the new document
        var targetFileName = Path.GetFileName(resolvedTargetPath);
        var newDocument = newSolution.GetProject(sourceDocument.Project.Id)!
            .AddDocument(targetFileName, newFileRoot.ToFullString(), filePath: resolvedTargetPath);
        newSolution = newDocument.Project.Solution;

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Move type '{typeName}' to {targetFileName}";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }
}
