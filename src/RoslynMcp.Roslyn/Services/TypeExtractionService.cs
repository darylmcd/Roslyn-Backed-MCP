using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class TypeExtractionService : ITypeExtractionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<TypeExtractionService> _logger;

    public TypeExtractionService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<TypeExtractionService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewExtractTypeAsync(
        string workspaceId, string filePath, string sourceTypeName,
        IReadOnlyList<string> memberNames, string newTypeName, string? newFilePath,
        CancellationToken ct)
    {
        if (memberNames.Count == 0)
            throw new ArgumentException("At least one member name must be specified.", nameof(memberNames));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created.");

        var typeDecl = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, sourceTypeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{sourceTypeName}' not found in {filePath}.");

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve type '{sourceTypeName}'.");

        // Find the member declarations to extract
        var memberNameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
        var membersToExtract = new List<MemberDeclarationSyntax>();
        var membersToKeep = new List<MemberDeclarationSyntax>();

        foreach (var member in typeDecl.Members)
        {
            var memberName = GetMemberName(member);
            if (memberName is not null && memberNameSet.Contains(memberName))
            {
                membersToExtract.Add(member);
                memberNameSet.Remove(memberName);
            }
            else
            {
                membersToKeep.Add(member);
            }
        }

        if (memberNameSet.Count > 0)
        {
            throw new InvalidOperationException(
                $"Members not found in type '{sourceTypeName}': {string.Join(", ", memberNameSet)}");
        }

        if (membersToExtract.Count == 0)
            throw new InvalidOperationException("No members matched for extraction.");

        // Determine target file path
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
        var resolvedTargetPath = newFilePath ?? Path.Combine(sourceDir, $"{newTypeName}.cs");
        resolvedTargetPath = Path.GetFullPath(resolvedTargetPath);

        // Build the new type declaration with extracted members
        // Strip accessibility modifiers from extracted members (make them public in the new type)
        var extractedMembers = membersToExtract.Select(m => EnsurePublicAccessibility(m)).ToList();

        TypeDeclarationSyntax newTypeDecl = SyntaxFactory.ClassDeclaration(newTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithMembers(SyntaxFactory.List(extractedMembers));

        // Build new file
        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        CompilationUnitSyntax newFileRoot;

        if (namespaceDecl is FileScopedNamespaceDeclarationSyntax fileScopedNs)
        {
            var newNs = SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl));
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else if (namespaceDecl is NamespaceDeclarationSyntax blockNs)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl));
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else
        {
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl))
                .NormalizeWhitespace();
        }

        // Remove extracted members from source type
        var updatedTypeDecl = typeDecl.WithMembers(SyntaxFactory.List(membersToKeep));

        // Add field and constructor parameter for the new type
        var fieldName = "_" + char.ToLowerInvariant(newTypeName[0]) + newTypeName[1..];
        var fieldDecl = SyntaxFactory.FieldDeclaration(
            SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(newTypeName))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(fieldName))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

        // Find existing constructor and add parameter, or create one
        var existingCtor = updatedTypeDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (existingCtor is not null)
        {
            var paramName = char.ToLowerInvariant(newTypeName[0]) + newTypeName[1..];
            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName(newTypeName));
            var updatedCtor = existingCtor.AddParameterListParameters(newParam);

            // Add assignment to constructor body
            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    SyntaxFactory.IdentifierName(paramName)));

            if (updatedCtor.Body is not null)
            {
                updatedCtor = updatedCtor.WithBody(updatedCtor.Body.AddStatements(assignment));
            }

            updatedTypeDecl = updatedTypeDecl.ReplaceNode(existingCtor, updatedCtor);
        }

        // Insert field at the top of the type
        updatedTypeDecl = updatedTypeDecl.WithMembers(
            updatedTypeDecl.Members.Insert(0, fieldDecl));

        // Replace in source root
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDecl, updatedTypeDecl);
        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add new document
        var targetFileName = Path.GetFileName(resolvedTargetPath);
        var newDoc = newSolution.GetProject(sourceDocument.Project.Id)!
            .AddDocument(targetFileName, newFileRoot.ToFullString(), filePath: resolvedTargetPath);
        newSolution = newDoc.Project.Solution;

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract {membersToExtract.Count} member(s) from '{sourceTypeName}' into new type '{newTypeName}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            _ => null
        };
    }

    private static MemberDeclarationSyntax EnsurePublicAccessibility(MemberDeclarationSyntax member)
    {
        // Remove existing access modifiers and add public
        var accessModifiers = new[]
        {
            SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword,
            SyntaxKind.InternalKeyword
        };

        var currentModifiers = member switch
        {
            MethodDeclarationSyntax m => m.Modifiers,
            PropertyDeclarationSyntax p => p.Modifiers,
            FieldDeclarationSyntax f => f.Modifiers,
            EventDeclarationSyntax e => e.Modifiers,
            _ => default
        };

        var newModifiers = currentModifiers
            .Where(m => !accessModifiers.Contains(m.Kind()))
            .Prepend(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        var tokenList = SyntaxFactory.TokenList(newModifiers);

        return member switch
        {
            MethodDeclarationSyntax m => m.WithModifiers(tokenList),
            PropertyDeclarationSyntax p => p.WithModifiers(tokenList),
            FieldDeclarationSyntax f => f.WithModifiers(tokenList),
            EventDeclarationSyntax e => e.WithModifiers(tokenList),
            _ => member
        };
    }
}
