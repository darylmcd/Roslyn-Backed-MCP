using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class ClassSplitOrchestrator : IClassSplitOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;

    public ClassSplitOrchestrator(IWorkspaceManager workspace, ICompositePreviewStore compositePreviewStore)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewSplitClassAsync(
        string workspaceId,
        string filePath,
        string typeName,
        IReadOnlyList<string> memberNames,
        string newFileName,
        CancellationToken ct)
    {
        if (memberNames.Count == 0)
        {
            throw new InvalidOperationException("Provide at least one member name to move into the partial class file.");
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in '{filePath}'.");

        var selectedMembers = typeDeclaration.Members
            .Where(member => GetMemberName(member) is string name && memberNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (selectedMembers.Length != memberNames.Count)
        {
            var foundNames = selectedMembers.Select(GetMemberName).Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
            var missingNames = memberNames.Where(name => !foundNames.Contains(name)).ToArray();
            throw new InvalidOperationException($"Member(s) not found in '{typeName}': {string.Join(", ", missingNames)}");
        }

        var partialOriginal = EnsurePartial(typeDeclaration.RemoveNodes(selectedMembers, SyntaxRemoveOptions.KeepExteriorTrivia)
            ?? throw new InvalidOperationException("Failed to remove the selected members from the original type."));
        var updatedRoot = root.ReplaceNode(typeDeclaration, partialOriginal);

        var partialNewType = EnsurePartial(typeDeclaration.WithMembers(SyntaxFactory.List(selectedMembers)));
        var namespaceName = GetNamespaceName(typeDeclaration);
        var partialCompilationUnit = CreateCompilationUnit(root, partialNewType, namespaceName);
        var newFilePath = Path.Combine(Path.GetDirectoryName(filePath)!, newFileName);
        // dependency-inversion-noisy-diff: preserve trivia in the original tree; only format the new partial's type subtree if needed.
        var newFileContent = partialCompilationUnit.ToFullString();

        var originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var updatedOriginalContent = updatedRoot.ToFullString();
        var mutations = new List<CompositeFileMutation>
        {
            new(filePath, updatedOriginalContent),
            new(newFilePath, newFileContent)
        };
        var changes = new List<FileChangeDto>
        {
            new(filePath, DiffGenerator.GenerateUnifiedDiff(originalContent, updatedOriginalContent, filePath)),
            new(newFilePath, DiffGenerator.GenerateUnifiedDiff(string.Empty, newFileContent, newFilePath))
        };

        var description = $"Split class '{typeName}' into partial file '{newFileName}'";
        var token = _compositePreviewStore.Store(workspaceId, _workspace.GetCurrentVersion(workspaceId), description, mutations);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => null
        };
    }

    private static TypeDeclarationSyntax EnsurePartial(TypeDeclarationSyntax declaration)
    {
        if (declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            return declaration;

        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.ElasticSpace);
        return declaration.WithModifiers(declaration.Modifiers.Add(partialToken));
    }

    private static string GetNamespaceName(TypeDeclarationSyntax declaration)
    {
        return declaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()?.Name.ToString() ?? string.Empty;
    }

    private static CompilationUnitSyntax CreateCompilationUnit(CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax declaration, string namespaceName)
    {
        var compilationUnit = SyntaxFactory.CompilationUnit().WithUsings(sourceRoot.Usings);
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(declaration));
        }

        var nsDecl = SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
            .WithNamespaceKeyword(
                SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(declaration));
        return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(nsDecl));
    }
}
